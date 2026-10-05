using FluentAssertions;
using MMV.DatabaseManager.CommandLine;

namespace MMV.DatabaseManager.Tests;

/// <summary>
/// P4-6B (C3) — point d'entrée : arguments → runner → code de sortie, sans serveur. La chaîne de connexion du
/// rôle migrateur vient de <c>MMV_MIGRATOR_CONNECTION_STRING</c>, jamais d'un argument ni de la variable des postes.
/// </summary>
public sealed class ProgramTests
{
    /// <summary>Hôte du domaine réservé <c>.invalid</c> (RFC 2606) : tout contact serveur échouerait (code 20).</summary>
    private static readonly IReadOnlyDictionary<string, string?> UnreachableServer = new Dictionary<string, string?>
    {
        [MigrationToolOptions.ConnectionStringVariableName] = "Host=mmv-migrator.invalid;Database=mmv;Username=x;Password=y;Timeout=2"
    };

    private static async Task<(int Code, string Output, string Error)> Run(
        IReadOnlyDictionary<string, string?> environment, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(Path.GetTempPath(), $"mmv-dbm-test-{Guid.NewGuid():N}.log");
        try
        {
            var code = await Program.RunAsync(args, environment, output, error, trace);
            return (code, output.ToString(), error.ToString());
        }
        finally
        {
            File.Delete(trace);
        }
    }

    [Fact]
    public async Task Missing_operator_exits_10()
    {
        var (code, _, error) = await Run(UnreachableServer, "migrate", "--backup-ref", "b", "--app-role", "r");

        code.Should().Be(10);
        error.Should().Contain("--operator");
    }

    [Fact]
    public async Task Missing_app_role_exits_10()
    {
        (await Run(UnreachableServer, "adopt-compatibility", "--operator", "OP", "--backup-ref", "b")).Code.Should().Be(10);
    }

    [Fact]
    public async Task Missing_migrator_connection_string_exits_10_and_never_falls_back_to_the_workstation_variable()
    {
        var environment = new Dictionary<string, string?>
        {
            ["MMV_DATABASE_CONNECTION_STRING"] = "Host=poste;Database=mmv"
        };

        var (code, _, error) = await Run(environment, "migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", "r");

        code.Should().Be(10);
        error.Should().Contain("MMV_MIGRATOR_CONNECTION_STRING").And.NotContain("Host=poste");
    }

    [Fact]
    public async Task Migrate_is_refused_with_code_11_before_any_server_contact()
    {
        // Le serveur est injoignable : un contact donnerait 20. Le code 11 prouve que la sauvegarde est refusée
        // AVANT toute connexion (étape 1 avant étape 2) — une référence qui n'est pas un manifeste vérifié est refusée.
        var (code, _, error) = await Run(UnreachableServer,
            "migrate", "--operator", "OP", "--backup-ref", "dump-1", "--app-role", "mmv_app");

        code.Should().Be(11);
        error.Should().Contain("--backup-ref");
    }

    [Fact]
    public async Task Migrate_with_an_absent_manifest_is_refused_with_code_11_before_any_server_contact()
    {
        var absent = Path.Combine(Path.GetTempPath(), $"mmv-absent-{Guid.NewGuid():N}.manifest.json");

        var (code, _, error) = await Run(UnreachableServer,
            "migrate", "--operator", "OP", "--backup-ref", absent, "--app-role", "mmv_app");

        code.Should().Be(11);
        error.Should().Contain("introuvable");
    }

    [Fact]
    public async Task Adopt_is_refused_with_code_11_before_any_server_contact()
    {
        (await Run(UnreachableServer,
            "adopt-compatibility", "--operator", "OP", "--backup-ref", "dump-1", "--app-role", "mmv_app")).Code.Should().Be(11);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("migrate")]
    public async Task Malformed_connection_string_exits_10_without_echoing_it(string verb)
    {
        var environment = new Dictionary<string, string?>
        {
            [MigrationToolOptions.ConnectionStringVariableName] = "Host=x;Password=secret;NotAKeyword=1"
        };

        var (code, output, error) = await Run(environment, verb, "--operator", "OP", "--backup-ref", "b", "--app-role", "r");

        code.Should().Be(10);
        (output + error).Should().NotContain("secret");
    }

    [Fact]
    public async Task Status_on_an_unreachable_server_exits_20()
    {
        (await Run(UnreachableServer, "status")).Code.Should().Be(20);
    }

    [Fact]
    public async Task Error_messages_never_echo_the_connection_string()
    {
        var (_, output, error) = await Run(UnreachableServer, "status");

        (output + error).Should().NotContain("Password=y").And.NotContain("mmv-migrator.invalid;");
    }

    [Fact]
    public void Exit_codes_match_the_plan_table()
    {
        ((int)MigrationExitCode.Success).Should().Be(0);
        ((int)MigrationExitCode.InvalidArguments).Should().Be(10);
        ((int)MigrationExitCode.BackupNotVerified).Should().Be(11);
        ((int)MigrationExitCode.LockNotAcquired).Should().Be(12);
        ((int)MigrationExitCode.MigrationFailed).Should().Be(13);
        ((int)MigrationExitCode.VerificationFailed).Should().Be(14);
        ((int)MigrationExitCode.MetadataInconsistent).Should().Be(15);
        ((int)MigrationExitCode.ServerUnreachable).Should().Be(20);
        ((int)MigrationExitCode.BackupFailed).Should().Be(21);
        ((int)MigrationExitCode.RestoreVerificationFailed).Should().Be(22);
    }
}
