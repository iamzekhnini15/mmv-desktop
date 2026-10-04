using System.Net.Sockets;
using System.Security.Authentication;
using FluentAssertions;
using MMV.Infrastructure.Configuration;
using Npgsql;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-8 (D-13, D-14, D-15) — résolution de production d'un poste et classification d'une indisponibilité. Aucune
/// connexion : sources injectées, exceptions construites.
/// </summary>
public sealed class WorkstationResolutionTests
{
    private const string Secret = "unit-Resolution-Secret-0123456789";

    private static Dictionary<string, string?> Env(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Value);

    private sealed class FakeSource(PostgreSqlConnectionSettings? settings, Exception? failure = null)
        : IWorkstationDatabaseSettingsSource
    {
        public bool Exists => settings is not null || failure is not null;

        public PostgreSqlConnectionSettings Load() => failure is not null ? throw failure : settings!;
    }

    private static readonly PostgreSqlConnectionSettings Settings = new("srv.lan", 5432, "mmv", "mmv_poste", Secret, null);

    [Fact]
    public void Workstation_file_selects_PostgreSql_with_the_secure_connection_string()
    {
        var options = DatabaseProviderResolver.ResolveWorkstation(Env(), new FakeSource(Settings));

        options.Provider.Should().Be(DatabaseProvider.PostgreSql);
        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString);
        builder.SslMode.Should().Be(SslMode.VerifyFull);
        builder.RequireAuth.Should().Be("ScramSHA256");
        builder.Password.Should().Be(Secret);
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("sqlite")]
    public void Workstation_file_and_an_explicit_provider_variable_are_never_arbitrated(string provider)
    {
        var act = () => DatabaseProviderResolver.ResolveWorkstation(
            Env((DatabaseProviderResolver.ProviderVariableName, provider)), new FakeSource(Settings));

        act.Should().Throw<DatabaseConfigurationException>().Which.Message.Should().Contain("concurrentes");
    }

    [Fact]
    public void Unreadable_workstation_file_blocks_and_never_falls_back_to_sqlite()
    {
        var act = () => DatabaseProviderResolver.ResolveWorkstation(Env(),
            new FakeSource(null, new DatabaseConfigurationException("Configuration PostgreSQL du poste inutilisable")));

        act.Should().Throw<DatabaseConfigurationException>();
    }

    [Fact]
    public void Environment_PostgreSql_is_hardened()
    {
        var options = DatabaseProviderResolver.ResolveWorkstation(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, $"Host=h;Database=d;Username=u;Password={Secret}")), null);

        new NpgsqlConnectionStringBuilder(options.ConnectionString).SslMode.Should().Be(SslMode.VerifyFull);
    }

    [Theory]
    [InlineData("Host=h;SSL Mode=Disable")]
    [InlineData("Host=h;Require Auth=MD5")]
    [InlineData("Host=h;NotAKeyword=1")]
    public void Environment_PostgreSql_with_a_weakened_or_malformed_string_is_refused(string connectionString)
    {
        var act = () => DatabaseProviderResolver.ResolveWorkstation(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, connectionString + ";Password=" + Secret)), null);

        act.Should().Throw<DatabaseConfigurationException>().Which.Message.Should().NotContain(Secret);
    }

    [Fact]
    public void Nothing_configured_keeps_the_historical_sqlite_default()
    {
        DatabaseProviderResolver.ResolveWorkstation(Env(), new FakeSource(null)).Provider.Should().Be(DatabaseProvider.Sqlite);
        DatabaseProviderResolver.ResolveWorkstation(Env(), null).Provider.Should().Be(DatabaseProvider.Sqlite);
    }

    [Fact]
    public void Environment_only_resolution_is_unchanged()
    {
        const string raw = "Host=localhost;Database=mmv-p4-8-fake";
        DatabaseProviderResolver.Resolve(Env(
            (DatabaseProviderResolver.ProviderVariableName, "postgresql"),
            (DatabaseProviderResolver.ConnectionStringVariableName, raw))).ConnectionString.Should().Be(raw);
    }

    // --- D-15 : classification (formes mesurées, rapport P4-8 §3) ---

    public static TheoryData<Exception, DatabaseUnavailableReason> Failures => new()
    {
        { new NpgsqlException("Exception while performing SSL handshake", new AuthenticationException("rejected")), DatabaseUnavailableReason.TlsValidationFailed },
        { new NpgsqlException("SSL connection requested. No SSL enabled connection from this host is configured."), DatabaseUnavailableReason.TlsUnavailable },
        { new PostgresException("password authentication failed", "FATAL", "FATAL", "28P01"), DatabaseUnavailableReason.AuthenticationFailed },
        { new PostgresException("no pg_hba.conf entry", "FATAL", "FATAL", "28000"), DatabaseUnavailableReason.AuthenticationFailed },
        { new NpgsqlException("\"MD5\" authentication method is not allowed. Allowed methods: ScramSHA256"), DatabaseUnavailableReason.AuthenticationMethodRefused },
        { new PostgresException("database does not exist", "FATAL", "FATAL", "3D000"), DatabaseUnavailableReason.DatabaseNotFound },
        { new NpgsqlException("Failed to connect", new TimeoutException()), DatabaseUnavailableReason.ServerUnreachable },
        { new NpgsqlException("Unknown host", new SocketException(11001)), DatabaseUnavailableReason.ServerUnreachable },
        { new NpgsqlException("something else"), DatabaseUnavailableReason.Unknown },
        { new InvalidOperationException("x"), DatabaseUnavailableReason.Unknown }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Connection_failures_are_classified(Exception failure, DatabaseUnavailableReason expected)
    {
        PostgreSqlConnectivityProbe.Classify(failure).Should().Be(expected);
    }

    [Fact]
    public void Every_reason_has_an_explicit_stop_message_without_secret()
    {
        foreach (var reason in Enum.GetValues<DatabaseUnavailableReason>())
        {
            PostgreSqlConnectivityProbe.Message(reason, "'srv:5432/mmv'").Should()
                .Contain("'srv:5432/mmv'").And.Contain("Démarrage arrêté");
        }

        PostgreSqlConnectivityProbe.Describe($"Host=srv;Port=5433;Database=mmv;Username=u;Password={Secret}")
            .Should().Be("'srv:5433/mmv'");
    }
}
