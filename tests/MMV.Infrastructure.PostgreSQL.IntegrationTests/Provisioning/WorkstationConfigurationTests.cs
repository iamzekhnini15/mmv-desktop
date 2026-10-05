using FluentAssertions;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

/// <summary>
/// P4-8 (D-13, DP-5) — configuration d'un poste de bout en bout : vérifiée avant écriture, identité privilégiée
/// refusée, puis relue par la résolution de production du poste et connectée. DPAPI n'existe que sous Windows
/// (prouvé par la suite unitaire Windows) : ici, le protecteur est un double réversible, seul écart.
/// </summary>
public sealed class WorkstationConfigurationTests : IAsyncLifetime
{
    private readonly TlsServer _server = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mmv-it-ws-{Guid.NewGuid():N}");
    private ProvisioningRequest _request = null!;

    public async Task InitializeAsync()
    {
        _request = _server.Request(TlsServer.Suffix());
        (await new PostgreSqlProvisioner(new MigrationJournal()).RunAsync(_request)).ExitCode.Should().Be(MigrationExitCode.Success);
    }

    public async Task DisposeAsync()
    {
        await _server.DropAsync(_request);
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private WorkstationDatabaseSettingsFile File() =>
        new(Path.Combine(_directory, WorkstationDatabaseSettingsFile.FileName), new ReversibleTestProtector());

    private PostgreSqlConnectionSettings Settings(string user, string secret) =>
        new(_server.Host, _server.Port, _request.Database, user, secret, _server.RootCertificate);

    [PostgreSqlTlsFact]
    public async Task Application_role_is_saved_then_resolved_and_connected_by_the_workstation()
    {
        var file = File();

        var result = await new WorkstationConfigurator(file, new MigrationJournal())
            .RunAsync(Settings(_request.AppRole, _request.AppPassword));

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        var options = DatabaseProviderResolver.ResolveWorkstation(new Dictionary<string, string?>(), file);
        options.Provider.Should().Be(DatabaseProvider.PostgreSql);
        await PostgreSqlConnectivityProbe.EnsureAvailableAsync(options.ConnectionString!);
    }

    [PostgreSqlTlsTheory]
    [InlineData("migrator")]
    [InlineData("admin")]
    public async Task Privileged_identities_are_never_saved_on_a_workstation(string identity)
    {
        var file = File();
        var settings = identity == "migrator"
            ? Settings(_request.MigratorRole, _request.MigratorPassword)
            : Settings(_server.Admin.Username!, _server.Admin.Password!);

        var result = await new WorkstationConfigurator(file, new MigrationJournal()).RunAsync(settings);

        result.ExitCode.Should().Be(MigrationExitCode.SecurityRefused);
        file.Exists.Should().BeFalse();
    }

    [PostgreSqlTlsFact]
    public async Task Wrong_secret_writes_nothing()
    {
        var file = File();

        var result = await new WorkstationConfigurator(file, new MigrationJournal())
            .RunAsync(Settings(_request.AppRole, "it-wrong-Secret-0123456789abcdef"));

        result.ExitCode.Should().Be(MigrationExitCode.ServerUnreachable);
        result.Message.Should().NotContain("it-wrong-Secret");
        file.Exists.Should().BeFalse();
    }

    /// <summary>Double de test : transformation réversible et authentifiée par un préfixe. Jamais en production.</summary>
    internal sealed class ReversibleTestProtector : IWorkstationSecretProtector
    {
        public string Scheme => "it-reversible";

        public byte[] Protect(byte[] plaintext) => [0x4D, .. plaintext.Select(b => (byte)(b ^ 0x5A))];

        public byte[] Unprotect(byte[] protectedData) => protectedData.Skip(1).Select(b => (byte)(b ^ 0x5A)).ToArray();
    }
}
