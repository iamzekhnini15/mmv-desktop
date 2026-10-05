using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Startup;

/// <summary>
/// P4-6C — démarrage d'un poste PostgreSQL contre un <b>vrai</b> serveur TLS, sous le <b>rôle applicatif</b> d'une
/// base provisionnée par l'outil réel : disponibilité (D-15), puis garde de compatibilité (DP-4) ; et invariants
/// physiques de la vérification post-migration de l'outil (portage de <c>VerifyAfterPreparation</c>).
/// </summary>
public sealed class ServerStartupTests : IAsyncLifetime
{
    private readonly TlsServer _server = new();
    private readonly ProvisioningRequest _request;

    public ServerStartupTests() => _request = _server.Request(TlsServer.Suffix());

    public async Task InitializeAsync() =>
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);

    public Task DisposeAsync() => _server.DropAsync(_request);

    private string App => _server.ConnectionString(_request.Database, _request.AppRole, TlsServer.AppSecret);

    /// <summary>Exactement ce que fait <c>App.axaml.cs</c> pour un poste PostgreSQL.</summary>
    private static ServerStartupBlock? Start(string connectionString) =>
        ServerStartupCheck.Run(connectionString, () =>
        {
            var options = new DbContextOptionsBuilder<OpticDbContext>();
            DatabaseProviderResolver.Configure(options,
                new DatabaseProviderOptions { Provider = DatabaseProvider.PostgreSql, ConnectionString = connectionString }, null);
            return new OpticDbContext(options.Options);
        });

    private async Task MigratedAsync() =>
        (await _server.MigrateAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);

    [PostgreSqlTlsFact]
    public async Task Provisioned_but_not_migrated_database_blocks_the_workstation_with_E2()
    {
        var block = Start(App);

        block.Should().NotBeNull();
        block!.Title.Should().Be("Mise à jour de la base requise");
        (await _server.ScalarAsync<long>(_request.Database, "SELECT count(*) FROM \"__EFMigrationsHistory\""))
            .Should().Be(0, "le poste ne migre jamais (DP-1)");
    }

    [PostgreSqlTlsFact]
    public async Task Migrated_database_starts_the_workstation_without_writing()
    {
        await MigratedAsync();
        var users = await _server.ScalarAsync<long>(_request.Database, "SELECT count(*) FROM \"Users\"");

        Start(App).Should().BeNull("E1 : démarrage normal");
        (await _server.ScalarAsync<long>(_request.Database, "SELECT count(*) FROM \"Users\""))
            .Should().Be(users, "aucun seed sur un serveur (D-12.3)");
    }

    [PostgreSqlTlsFact]
    public async Task Maintenance_in_progress_blocks_the_workstation_with_E5()
    {
        await MigratedAsync();
        await _server.ExecuteAsync(_request.Database, "UPDATE mmv_meta.schema_compatibility SET maintenance_started_at = now()");

        Start(App)!.Title.Should().Be("Maintenance en cours");
    }

    [PostgreSqlTlsFact]
    public async Task Newer_schema_beyond_the_window_blocks_with_E3b_and_within_the_window_starts()
    {
        await MigratedAsync();
        await _server.ExecuteAsync(_request.Database,
            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('29991231235959_FromANewerRelease', '10.0.12')",
            "UPDATE mmv_meta.schema_compatibility SET schema_version = '99.0.0', minimum_supported_version = '99.0.0'");

        Start(App)!.Title.Should().Be("Mise à jour du poste requise");

        await _server.ExecuteAsync(_request.Database,
            $"UPDATE mmv_meta.schema_compatibility SET minimum_supported_version = '{ApplicationVersion.Current}'");
        Start(App).Should().BeNull("E3a : poste dans la fenêtre N-1, démarrage normal");
    }

    [PostgreSqlTlsFact]
    public void Unreachable_or_refused_server_blocks_without_retry_and_without_echoing_the_secret()
    {
        var wrong = _server.ConnectionString(_request.Database, _request.AppRole, "it-wrong-Secret-0123456789abcd");

        var block = Start(wrong);

        block!.Title.Should().Be("Base centrale indisponible");
        (block.Message + block.Action).Should().NotContain("it-wrong-Secret").And.NotContain(TlsServer.AppSecret);
        block.Action.Should().Contain("Aucune nouvelle tentative");
    }

    [PostgreSqlTlsFact]
    public void Privileged_identity_on_a_workstation_is_refused()
    {
        Start(_server.AdminOn(_request.Database))!.Title.Should().Be("Configuration du poste refusée");
    }

    // ---- invariants physiques de l'outil (étape 9) ------------------------------------------------------------

    private async Task<SchemaVerificationResult> VerifyAsync()
    {
        await using var session = PostgreSqlMigrationSession.Create(
            builder => DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
            {
                Provider = DatabaseProvider.PostgreSql,
                ConnectionString = _server.ConnectionString(_request.Database, _request.MigratorRole, TlsServer.MigratorSecret)
            }),
            MigrationRunner.DefaultLockTimeout);
        await session.OpenForStatusAsync();
        return await session.Ports.Verification.VerifyAsync(_request.AppRole);
    }

    [PostgreSqlTlsFact]
    public async Task Production_schema_satisfies_the_physical_invariants()
    {
        await MigratedAsync();

        var result = await VerifyAsync();

        result.Succeeded.Should().BeTrue(result.Failure);
    }

    [PostgreSqlTlsFact]
    public async Task Missing_or_non_unique_login_index_fails_the_verification()
    {
        await MigratedAsync();
        await _server.ExecuteAsync(_request.Database, "DROP INDEX idx_users_normalized_username_unique");
        (await VerifyAsync()).Failure.Should().Contain("idx_users_normalized_username_unique");

        await _server.ExecuteAsync(_request.Database,
            "CREATE INDEX idx_users_normalized_username_unique ON \"Users\" (\"NormalizedUsername\")");
        (await VerifyAsync()).Failure.Should().Contain("idx_users_normalized_username_unique", "un index non unique ne protège pas le login");
    }

    [PostgreSqlTlsFact]
    public async Task Low_stock_index_with_a_weakened_predicate_fails_the_verification()
    {
        await MigratedAsync();
        await _server.ExecuteAsync(_request.Database,
            "DROP INDEX idx_notifications_active_low_stock_unique",
            "CREATE UNIQUE INDEX idx_notifications_active_low_stock_unique ON \"Notifications\" (\"Type\", \"EntityType\", \"EntityId\") " +
            "WHERE \"Type\" = 'LowStock' AND \"EntityType\" = 'Product' AND \"EntityId\" IS NOT NULL");

        (await VerifyAsync()).Failure.Should().Contain("idx_notifications_active_low_stock_unique");
    }

    [PostgreSqlTlsFact]
    public async Task Missing_resolution_column_fails_the_verification()
    {
        await MigratedAsync();
        await _server.ExecuteAsync(_request.Database, "ALTER TABLE \"Notifications\" DROP COLUMN \"ResolvedAt\" CASCADE");

        (await VerifyAsync()).Failure.Should().Contain("Notifications.ResolvedAt");
    }
}
