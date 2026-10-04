using FluentAssertions;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Provisioning;

/// <summary>
/// P4-8 (D-14, D-15) — connexion d'un poste sur un vrai serveur TLS : chaque cas d'échec est <b>classé</b>, aucun
/// ne retombe sur une connexion non vérifiée, non chiffrée ou MD5, et aucun message ne porte le secret. Épingle les
/// formes d'exception mesurées (PostgreSQL 17.10, Npgsql 10.0.3).
/// </summary>
public sealed class PostgreSqlConnectivityProbeTests : IAsyncLifetime
{
    private const string Secret = "it-probe-Secret-0123456789abcdef";
    private const string Md5ProbeRole = "mmv_tls_md5_probe";

    private readonly TlsServer _server = new();
    private readonly string _role = "mmv_it_probe_" + TlsServer.Suffix();

    public Task InitializeAsync() => _server.ExecuteAsync("postgres",
        $"CREATE ROLE {TlsServer.Quote(_role)} LOGIN PASSWORD {TlsServer.Literal(Secret)}");

    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(Valid()));
        return _server.ExecuteAsync("postgres", $"DROP ROLE IF EXISTS {TlsServer.Quote(_role)}");
    }

    public static DatabaseUnavailableReason? ReasonOf(Exception? exception) =>
        exception is null ? null : PostgreSqlConnectivityProbe.Classify(exception);

    private string Valid(Action<NpgsqlConnectionStringBuilder>? mutate = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(_server.ConnectionString("postgres", _role, Secret)) { Timeout = 5 };
        mutate?.Invoke(builder);
        return builder.ConnectionString;
    }

    private static async Task<DatabaseUnavailableException?> ProbeAsync(string connectionString)
    {
        try
        {
            await PostgreSqlConnectivityProbe.EnsureAvailableAsync(connectionString);
            return null;
        }
        catch (DatabaseUnavailableException exception)
        {
            return exception;
        }
    }

    [PostgreSqlTlsFact]
    public async Task Valid_VerifyFull_scram_connection_of_an_unprivileged_role_is_accepted()
    {
        (await ProbeAsync(Valid())).Should().BeNull();

        await using var connection = new NpgsqlConnection(Valid());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection);
        ((bool)(await command.ExecuteScalarAsync())!).Should().BeTrue("la session est réellement chiffrée");
    }

    [PostgreSqlTlsTheory]
    [InlineData("wrong-ca")]
    [InlineData("hostname")]
    [InlineData("system-store")]
    [InlineData("expired")]
    public async Task Untrusted_mismatched_or_expired_certificates_are_refused(string scenario)
    {
        var connectionString = Valid(b =>
        {
            switch (scenario)
            {
                case "wrong-ca": b.RootCertificate = PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.TlsWrongCaVariableName); break;
                case "hostname": b.Host = "127.0.0.1"; break; // le certificat ne porte que DNS:localhost
                case "system-store": b.RootCertificate = null; break; // l'autorité de test n'est pas dans le magasin
                case "expired": b.Port = int.Parse(PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.TlsExpiredPortVariableName)); break;
            }
        });

        var failure = await ProbeAsync(connectionString);

        failure.Should().NotBeNull();
        failure!.Reason.Should().Be(DatabaseUnavailableReason.TlsValidationFailed);
        failure.Message.Should().NotContain(Secret);
    }

    [PostgreSqlTlsFact]
    public async Task Server_without_TLS_is_refused_and_never_used_unencrypted()
    {
        var plain = new NpgsqlConnectionStringBuilder(PostgreSqlTestEnvironment.GetRequiredConnectionString());

        var failure = await ProbeAsync(PostgreSqlConnectionSecurity.Harden(plain.ConnectionString));

        failure!.Reason.Should().Be(DatabaseUnavailableReason.TlsUnavailable);
        failure.Message.Should().NotContain(plain.Password!);
    }

    [PostgreSqlTlsFact]
    public async Task Server_requesting_md5_is_refused_by_the_client()
    {
        // pg_hba du serveur de test : « hostssl all mmv_tls_md5_probe all md5 ».
        await _server.ExecuteAsync("postgres",
            $"DROP ROLE IF EXISTS {Md5ProbeRole}",
            "SET password_encryption = 'md5'",
            $"CREATE ROLE {Md5ProbeRole} LOGIN PASSWORD {TlsServer.Literal(Secret)}");
        try
        {
            var failure = await ProbeAsync(_server.ConnectionString("postgres", Md5ProbeRole, Secret));

            failure!.Reason.Should().Be(DatabaseUnavailableReason.AuthenticationMethodRefused);
        }
        finally
        {
            await _server.ExecuteAsync("postgres", $"DROP ROLE IF EXISTS {Md5ProbeRole}");
        }
    }

    [PostgreSqlTlsTheory]
    [InlineData("wrong-password", DatabaseUnavailableReason.AuthenticationFailed)]
    [InlineData("unknown-role", DatabaseUnavailableReason.AuthenticationFailed)]
    [InlineData("unknown-database", DatabaseUnavailableReason.DatabaseNotFound)]
    [InlineData("closed-port", DatabaseUnavailableReason.ServerUnreachable)]
    [InlineData("unknown-host", DatabaseUnavailableReason.ServerUnreachable)]
    public async Task Connection_failures_are_classified_without_retry_and_without_secret(string scenario, DatabaseUnavailableReason expected)
    {
        var connectionString = Valid(b =>
        {
            switch (scenario)
            {
                case "wrong-password": b.Password = "it-wrong-Secret-0123456789abcdef"; break;
                case "unknown-role": b.Username = "mmv_it_ghost_" + TlsServer.Suffix(); break;
                case "unknown-database": b.Database = "mmv_it_ghost_" + TlsServer.Suffix(); break;
                case "closed-port": b.Port = 1; b.Timeout = 3; break;
                case "unknown-host": b.Host = "mmv-server.invalid"; break;
            }
        });

        var failure = await ProbeAsync(connectionString);

        failure!.Reason.Should().Be(expected);
        failure.Message.Should().NotContain(Secret).And.Contain("Démarrage arrêté");
    }

    [PostgreSqlTlsFact]
    public async Task Privileged_identity_is_refused_on_a_workstation()
    {
        var failure = await ProbeAsync(_server.Admin.ConnectionString);

        failure!.Reason.Should().Be(DatabaseUnavailableReason.PrivilegedIdentity);
        failure.Message.Should().NotContain(_server.Admin.Password!);
    }
}
