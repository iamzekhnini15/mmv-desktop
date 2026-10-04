using FluentAssertions;
using MMV.Infrastructure.Configuration;
using Npgsql;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P4-8 (ADR-PROD-DB-010, D-14) — politique de connexion PostgreSQL de production : TLS VerifyFull et SCRAM-SHA-256
/// imposés ; défauts relevés, valeurs explicitement plus faibles refusées ; aucun secret restitué. Aucune connexion.
/// </summary>
public sealed class PostgreSqlConnectionSecurityTests
{
    private const string Secret = "unit-Secret-0123456789abcdef";

    private static PostgreSqlConnectionSettings Settings(string? root = null) =>
        new("db.magasin.lan", 5432, "mmv", "mmv_poste", Secret, root);

    [Fact]
    public void Built_connection_string_requires_VerifyFull_and_scram_with_a_bounded_timeout()
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgreSqlConnectionSecurity.BuildConnectionString(Settings()));

        builder.SslMode.Should().Be(SslMode.VerifyFull);
        builder.RequireAuth.Should().Be("ScramSHA256");
        builder.Timeout.Should().Be(PostgreSqlConnectionSecurity.ConnectTimeoutSeconds);
        builder.Host.Should().Be("db.magasin.lan");
        builder.RootCertificate.Should().BeNull("sans autorité fournie, le magasin du système valide le certificat");
    }

    [Fact]
    public void Built_connection_string_carries_the_root_certificate()
    {
        var root = Path.GetTempFileName();
        try
        {
            new NpgsqlConnectionStringBuilder(PostgreSqlConnectionSecurity.BuildConnectionString(Settings(root)))
                .RootCertificate.Should().Be(root);
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Theory]
    [InlineData("Host=h;Database=d")]
    [InlineData("Host=h;Database=d;SSL Mode=Prefer")]
    [InlineData("Host=h;Database=d;SSL Mode=VerifyFull;Require Auth=ScramSHA256")]
    [InlineData("Host=h;Database=d;sslmode=VerifyFull;RequireAuth=ScramSHA256")]
    public void Harden_raises_defaults_to_VerifyFull_and_scram(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(PostgreSqlConnectionSecurity.Harden(connectionString));

        builder.SslMode.Should().Be(SslMode.VerifyFull);
        builder.RequireAuth.Should().BeEquivalentTo("ScramSHA256");
    }

    [Theory]
    [InlineData("Host=h;SSL Mode=Disable")]
    [InlineData("Host=h;SSL Mode=Allow")]
    [InlineData("Host=h;SSL Mode=Require")]
    [InlineData("Host=h;SSL Mode=VerifyCA")]
    [InlineData("Host=h;sslmode=disable")]
    public void Harden_refuses_any_weaker_TLS_mode_without_fallback(string connectionString)
    {
        var act = () => PostgreSqlConnectionSecurity.Harden(connectionString + ";Password=" + Secret);

        act.Should().Throw<DatabaseConfigurationException>()
            .Which.Message.Should().Contain("VerifyFull").And.NotContain(Secret);
    }

    [Theory]
    [InlineData("MD5")]
    [InlineData("Password")]
    [InlineData("ScramSHA256,MD5")]
    [InlineData("!MD5")]
    public void Harden_refuses_any_authentication_other_than_scram(string requireAuth)
    {
        var act = () => PostgreSqlConnectionSecurity.Harden($"Host=h;Password={Secret};Require Auth={requireAuth}");

        act.Should().Throw<DatabaseConfigurationException>()
            .Which.Message.Should().Contain("MD5").And.NotContain(Secret);
    }

    [Theory]
    [InlineData("Host=h;NotAKeyword=1;Password=" + Secret)]
    [InlineData("Host=h;Port=abc;Password=" + Secret)]
    [InlineData("Host=h;Require Auth=All;Password=" + Secret)]
    [InlineData("Host=h;sslmode=verify-full;Password=" + Secret)]
    public void Malformed_connection_string_is_refused_without_echoing_it(string connectionString)
    {
        var act = () => PostgreSqlConnectionSecurity.Harden(connectionString);

        act.Should().Throw<ArgumentException>().Which.Message.Should().NotContain(Secret).And.NotContain("Host=h");
    }

    [Theory]
    [InlineData("", 5432, "mmv", "r", "Hôte")]
    [InlineData("h", 0, "mmv", "r", "Port")]
    [InlineData("h", 70000, "mmv", "r", "Port")]
    [InlineData("h", 5432, "", "r", "base")]
    [InlineData("h", 5432, "mmv", "", "Rôle")]
    [InlineData("h", 5432, "mmv", "a_role_name_that_is_far_too_long_for_a_postgresql_identifier_max", "Rôle")]
    [InlineData("h\n", 5432, "mmv", "r", "Hôte")]
    public void Unusable_settings_are_refused(string host, int port, string database, string user, string expected)
    {
        var act = () => new PostgreSqlConnectionSettings(host, port, database, user, Secret, null).Validate();

        act.Should().Throw<DatabaseConfigurationException>().Which.Message.Should().Contain(expected).And.NotContain(Secret);
    }

    [Theory]
    [InlineData("relative/ca.crt")]
    [InlineData("C:\\mmv-p4-8-absent\\ca.crt")]
    public void Root_certificate_must_be_an_existing_absolute_file(string root)
    {
        var act = () => Settings(root).Validate();

        act.Should().Throw<DatabaseConfigurationException>().Which.Message.Should().Contain("Certificat racine");
    }

    [Fact]
    public void Empty_secret_is_refused_and_ToString_never_shows_the_secret()
    {
        var act = () => new PostgreSqlConnectionSettings("h", 5432, "mmv", "r", "", null).Validate();

        act.Should().Throw<DatabaseConfigurationException>();
        Settings().ToString().Should().NotContain(Secret).And.Contain("mmv_poste@db.magasin.lan:5432/mmv");
    }
}
