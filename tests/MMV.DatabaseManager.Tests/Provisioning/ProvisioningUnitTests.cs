using FluentAssertions;
using MMV.DatabaseManager.Provisioning;

namespace MMV.DatabaseManager.Tests.Provisioning;

/// <summary>
/// P4-8 (ADR-PROD-DB-010) — briques de provisioning sans serveur : vérificateur SCRAM-SHA-256 (vecteur connu calculé
/// par une implémentation indépendante, Python <c>hashlib</c>), politique des secrets de rôle, audit de conformité
/// du serveur, contrôles locaux d'une demande (noms réservés, identités distinctes, secrets distincts).
/// </summary>
public sealed class ProvisioningUnitTests
{
    // Python 3 : hashlib.pbkdf2_hmac('sha256', pw, bytes(range(16)), 4096, 32) puis RFC 5802 (StoredKey, ServerKey).
    private const string KnownAnswer =
        "SCRAM-SHA-256$4096:AAECAwQFBgcICQoLDA0ODw==$TsRS2KU99Ogl5PTrAaDLNSt8dZwC1k1vNE6tou9LPEM=:SEveG7MdkOInjq+m0Wxoo9T7ElRVYM5vyO6xVWEDA+4=";

    [Fact]
    public void Scram_verifier_matches_an_independent_known_answer()
    {
        ScramSha256Verifier.Create("MMV-scram-known-answer-0001!", Enumerable.Range(0, 16).Select(i => (byte)i).ToArray())
            .Should().Be(KnownAnswer);
    }

    [Fact]
    public void Scram_verifier_is_salted_randomly_and_never_contains_the_secret()
    {
        const string secret = "unit-Scram-Secret-0123456789abcd";

        var first = ScramSha256Verifier.Create(secret);
        var second = ScramSha256Verifier.Create(secret);

        first.Should().StartWith("SCRAM-SHA-256$4096:").And.NotContain(secret);
        first.Should().NotBe(second);
        first.Should().MatchRegex(@"^SCRAM-SHA-256\$4096:[A-Za-z0-9+/=]{24}\$[A-Za-z0-9+/=]{44}:[A-Za-z0-9+/=]{44}$");
    }

    [Fact]
    public void Scram_verifier_refuses_fewer_iterations_than_the_server_default()
    {
        var act = () => ScramSha256Verifier.Create("unit-Scram-Secret-0123456789abcd", iterations: 1000);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("short-Secret-1")]
    [InlineData("has a space inside 0123456789")]
    [InlineData("accentué-Secret-0123456789abcdef")]
    [InlineData("tab\tinside-Secret-0123456789abc")]
    [InlineData("")]
    public void Role_secret_policy_refuses_short_spaced_non_ascii_or_control_secrets(string secret)
    {
        RoleSecretPolicy.IsValid(secret).Should().BeFalse();
        ((Action)(() => ScramSha256Verifier.Create(secret))).Should().Throw<ArgumentException>()
            .Which.Message.Should().NotContain(secret == "" ? "\u0001" : secret);
    }

    // --- Audit du serveur ---

    private static readonly string[] Roles = ["mig", "app", "bkp"];

    private static ServerSecuritySnapshot Compliant(params HbaRule[] extra) => new(true, "on", "scram-sha-256",
        [
            new HbaRule(1, "local", ["all"], ["all"], "trust", null),
            new HbaRule(2, "hostssl", ["all"], ["all"], "scram-sha-256", null),
            .. extra
        ]);

    [Fact]
    public void Compliant_server_has_no_finding()
    {
        ServerSecurityAudit.Evaluate(Compliant(), "mmv", Roles).Should().BeEmpty();
    }

    [Fact]
    public void Server_level_weaknesses_are_all_reported()
    {
        var findings = ServerSecurityAudit.Evaluate(new ServerSecuritySnapshot(false, "off", "md5", []), "mmv", Roles);

        findings.Should().HaveCount(4);
        findings.Should().Contain(f => f.Contains("superutilisateur"))
            .And.Contain(f => f.Contains("ssl = off"))
            .And.Contain(f => f.Contains("password_encryption = md5"))
            .And.Contain(f => f.Contains("pg_hba illisible"));
    }

    [Theory]
    [InlineData("host", "scram-sha-256", "sans TLS")]
    [InlineData("hostnossl", "scram-sha-256", "sans TLS")]
    [InlineData("hostgssenc", "scram-sha-256", "sans TLS")]
    [InlineData("hostssl", "md5", "md5")]
    [InlineData("hostssl", "password", "password")]
    [InlineData("hostssl", "trust", "trust")]
    public void Network_rules_allowing_cleartext_transport_or_weak_authentication_are_refused(string type, string method, string expected)
    {
        ServerSecurityAudit.Evaluate(Compliant(new HbaRule(9, type, ["all"], ["app"], method, null)), "mmv", Roles)
            .Should().ContainSingle().Which.Should().Contain("ligne 9").And.Contain(expected);
    }

    [Theory]
    [InlineData("all", "+staff")]
    [InlineData("all", "@users.txt")]
    [InlineData("sameuser", "all")]
    [InlineData("mmv", "/^m.*$")]
    public void Group_file_regex_and_keyword_entries_are_counted_conservatively(string database, string user)
    {
        ServerSecurityAudit.Evaluate(Compliant(new HbaRule(9, "host", [database], [user], "md5", null)), "mmv", Roles)
            .Should().HaveCount(2);
    }

    [Theory]
    [InlineData("host", "all", "someone_else", "md5")]
    [InlineData("host", "other_db", "all", "md5")]
    [InlineData("host", "replication", "all", "md5")]
    [InlineData("host", "all", "all", "reject")]
    [InlineData("local", "all", "all", "trust")]
    public void Rules_that_cannot_admit_a_MMV_network_session_are_ignored(string type, string database, string user, string method)
    {
        ServerSecurityAudit.Evaluate(Compliant(new HbaRule(9, type, [database], [user], method, null)), "mmv", Roles)
            .Should().BeEmpty();
    }

    [Fact]
    public void Invalid_pg_hba_line_is_a_finding()
    {
        ServerSecurityAudit.Evaluate(Compliant(new HbaRule(7, "", [], [], null, "invalid authentication method")), "mmv", Roles)
            .Should().ContainSingle().Which.Should().Contain("ligne 7 invalide");
    }

    // --- Demande ---

    private static ProvisioningRequest Request(Action<RequestOverrides>? change = null)
    {
        var o = new RequestOverrides();
        change?.Invoke(o);
        return new ProvisioningRequest
        {
            Host = "srv", Port = 5432, AdminUser = o.Admin, AdminPassword = "admin", AdminDatabase = "postgres",
            Database = o.Database, MigratorRole = o.Migrator, MigratorPassword = o.MigratorSecret,
            AppRole = o.App, AppPassword = o.AppSecret, BackupRole = o.Backup, BackupPassword = o.BackupSecret,
            Operator = "OP"
        };
    }

    private sealed class RequestOverrides
    {
        public string Admin { get; set; } = "postgres";
        public string Database { get; set; } = "mmv";
        public string Migrator { get; set; } = "mig";
        public string App { get; set; } = "app";
        public string Backup { get; set; } = "bkp";
        public string MigratorSecret { get; set; } = "unit-Migrator-Secret-0123456789";
        public string AppSecret { get; set; } = "unit-Application-Secret-012345";
        public string BackupSecret { get; set; } = "unit-Backup-Secret-0123456789ab";
    }

    [Fact]
    public void Valid_request_passes_local_checks_and_its_text_carries_no_secret()
    {
        var request = Request();

        PostgreSqlProvisioner.Validate(request).Should().BeNull();
        request.ToString().Should().NotContain("Secret").And.NotContain("admin\"");
    }

    [Theory]
    [InlineData("pg_app")]
    [InlineData("PG_app")]
    [InlineData("public")]
    public void Reserved_names_are_refused(string name)
    {
        PostgreSqlProvisioner.Validate(Request(o => o.App = name)).Should().Contain("réservé");
    }

    [Fact]
    public void Roles_and_administrator_must_be_four_distinct_identities()
    {
        PostgreSqlProvisioner.Validate(Request(o => o.App = "mig")).Should().Contain("distinctes");
        PostgreSqlProvisioner.Validate(Request(o => o.Migrator = "postgres")).Should().Contain("distinctes");
    }

    [Fact]
    public void Each_role_needs_its_own_compliant_secret()
    {
        PostgreSqlProvisioner.Validate(Request(o => o.AppSecret = "short")).Should().Be(RoleSecretPolicy.Requirement);
        PostgreSqlProvisioner.Validate(Request(o => o.AppSecret = o.BackupSecret)).Should().Contain("propre secret");
    }

    [Fact]
    public void Too_long_or_control_names_are_refused()
    {
        PostgreSqlProvisioner.Validate(Request(o => o.Database = new string('d', 64))).Should().NotBeNull();
        PostgreSqlProvisioner.Validate(Request(o => o.Backup = "bkp\n")).Should().NotBeNull();
    }
}
