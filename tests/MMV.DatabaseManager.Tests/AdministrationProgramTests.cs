using FluentAssertions;
using MMV.DatabaseManager.CommandLine;

namespace MMV.DatabaseManager.Tests;

/// <summary>
/// P4-8 — point d'entrée des verbes d'administration et durcissement de la chaîne du migrateur (D-14), sans serveur :
/// secrets lus sur l'entrée standard et jamais restitués, chaîne affaiblie refusée avant tout contact.
/// </summary>
public sealed class AdministrationProgramTests
{
    private const string Secret = "unit-Program-Secret-0123456789ab";

    private static async Task<(int Code, string Output, string Error)> Run(
        IReadOnlyDictionary<string, string?> environment, string stdin, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var trace = Path.Combine(Path.GetTempPath(), $"mmv-dbm-admin-{Guid.NewGuid():N}.log");
        try
        {
            var code = await Program.RunAsync(args, environment, output, error, trace, new StringReader(stdin));
            var traced = File.Exists(trace) ? await File.ReadAllTextAsync(trace) : string.Empty;
            return (code, output + traced, error.ToString());
        }
        finally
        {
            File.Delete(trace);
        }
    }

    private static readonly string[] Provision =
    [
        "provision", "--host", "mmv-server.invalid", "--admin-user", "postgres", "--database", "mmv",
        "--migrator-role", "m", "--app-role", "a", "--backup-role", "b", "--operator", "OP"
    ];

    private static readonly IReadOnlyDictionary<string, string?> NoEnvironment = new Dictionary<string, string?>();

    [Fact]
    public async Task Missing_secret_on_stdin_exits_10_before_any_contact()
    {
        var (code, _, error) = await Run(NoEnvironment, "only-the-admin-secret\n", Provision);

        code.Should().Be(10);
        error.Should().Contain("Secret manquant").And.Contain("migrateur");
    }

    [Fact]
    public async Task Unreachable_server_exits_20_and_never_echoes_any_secret()
    {
        var stdin = string.Join('\n', "admin-" + Secret, "mig-" + Secret, "app-" + Secret, "bkp-" + Secret);

        var (code, output, error) = await Run(NoEnvironment, stdin, Provision);

        code.Should().Be(20);
        (output + error).Should().NotContain(Secret).And.Contain("injoignable");
    }

    [Fact]
    public async Task Invalid_administration_arguments_exit_10_with_usage()
    {
        var (code, _, error) = await Run(NoEnvironment, "", "provision", "--host", "h");

        code.Should().Be(10);
        error.Should().Contain("Verbes d'administration");
    }

    [Fact]
    public async Task Bootstrap_without_the_migrator_connection_string_exits_10()
    {
        var (code, _, error) = await Run(NoEnvironment, "", "bootstrap-admin", "--username", "p", "--operator", "OP");

        code.Should().Be(10);
        error.Should().Contain(MigrationToolOptions.ConnectionStringVariableName);
    }

    [Theory]
    [InlineData("bootstrap-admin", "--username", "p", "--operator", "OP")]
    [InlineData("status")]
    [InlineData("migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", "r")]
    public async Task Weakened_migrator_connection_string_is_refused_before_any_contact(params string[] args)
    {
        var environment = new Dictionary<string, string?>
        {
            [MigrationToolOptions.ConnectionStringVariableName] = $"Host=mmv-migrator.invalid;SSL Mode=Disable;Password={Secret}"
        };

        var (code, output, error) = await Run(environment, "", args);

        code.Should().Be(10, "TLS VerifyFull est imposé à la chaîne du migrateur (D-14), avant tout contact serveur");
        error.Should().Contain("VerifyFull");
        (output + error).Should().NotContain(Secret);
    }

    [Fact]
    public async Task Bootstrap_on_an_unreachable_server_exits_20_and_never_echoes_the_secret()
    {
        var environment = new Dictionary<string, string?>
        {
            [MigrationToolOptions.ConnectionStringVariableName] = $"Host=mmv-migrator.invalid;Database=mmv;Username=m;Password={Secret};Timeout=2"
        };

        var (code, output, error) = await Run(environment, "Initial-Admin-2026\nInitial-Admin-2026\n",
            "bootstrap-admin", "--username", "patron", "--operator", "OP");

        code.Should().Be(20);
        (output + error).Should().NotContain(Secret).And.NotContain("Initial-Admin-2026");
    }

    [Fact]
    public async Task Bootstrap_refuses_a_weak_initial_secret_before_any_contact()
    {
        var environment = new Dictionary<string, string?>
        {
            [MigrationToolOptions.ConnectionStringVariableName] = $"Host=mmv-migrator.invalid;Database=mmv;Username=m;Password={Secret}"
        };

        var (code, _, error) = await Run(environment, "weak\nweak\n", "bootstrap-admin", "--username", "patron", "--operator", "OP");

        code.Should().Be(10);
        error.Should().Contain("Mot de passe initial refusé");
    }

    [Fact]
    public async Task Configure_workstation_against_an_unreachable_server_writes_nothing()
    {
        var (code, output, error) = await Run(NoEnvironment, Secret + "\n",
            "configure-workstation", "--host", "mmv-server.invalid", "--database", "mmv", "--username", "mmv_poste");

        code.Should().Be(20);
        (output + error).Should().NotContain(Secret).And.Contain("Aucune configuration n'a été écrite");
    }
}
