using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.CommandLine;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// P4-6B (H12, R-1 ; §6.2 du plan) — U-C9 : aucun contournement de la vérification de sauvegarde. La seule
/// implémentation de production refuse toujours ; <c>Program.cs</c> ne construit qu'elle ; ni option, ni
/// variable d'environnement, ni compilation conditionnelle ne l'évitent. Le vérificateur réel est P4-9.
/// </summary>
public sealed class BackupSeamTests
{
    [Theory]
    [InlineData("dump-2026-10-04")]
    [InlineData("")]
    [InlineData("verified")]
    public async Task Refusing_verification_always_refuses(string reference)
    {
        var result = await new RefusingBackupVerification().VerifyAsync(reference, CancellationToken.None);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("P4-9");
    }

    [Fact]
    public void Refusing_verification_is_the_only_implementation_in_the_tool()
    {
        typeof(RefusingBackupVerification).Assembly.GetTypes()
            .Where(t => typeof(IBackupVerification).IsAssignableFrom(t) && !t.IsInterface)
            .Should().Equal(typeof(RefusingBackupVerification));
    }

    [Fact]
    public void Program_constructs_only_the_refusing_verification()
    {
        var program = ToolSource("Program.cs");

        Regex.Matches(program, @"new\s+RefusingBackupVerification\s*\(").Should().HaveCount(1);
        program.Should().NotMatchRegex(@"IBackupVerification\s+\w+\s*=\s*(?!new\s+RefusingBackupVerification)");
    }

    [Fact]
    public void No_conditional_compilation_and_a_single_environment_variable()
    {
        var sources = Directory.GetFiles(ToolDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetRelativePath(ToolDirectory(), f), File.ReadAllText);

        sources.Should().NotBeEmpty();
        sources.Where(s => Regex.IsMatch(s.Value, @"^\s*#\s*if\b", RegexOptions.Multiline))
            .Select(s => s.Key).Should().BeEmpty("aucun #if ne doit pouvoir changer la vérification de sauvegarde");

        var environmentReads = sources
            .SelectMany(s => Regex.Matches(s.Value, @"GetEnvironmentVariable\s*\(([^)]*)\)").Select(m => (s.Key, Arg: m.Groups[1].Value.Trim())))
            .ToList();
        environmentReads.Should().OnlyContain(r => r.Arg == "MigrationToolOptions.ConnectionStringVariableName",
            "la seule variable lue par l'outil est la chaîne de connexion du rôle migrateur");
        MigrationToolOptions.ConnectionStringVariableName.Should().Be("MMV_MIGRATOR_CONNECTION_STRING");
    }

    [Fact]
    public void Options_expose_no_bypass()
    {
        typeof(MigrationToolOptions).GetProperties().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Skip") || n.Contains("Force") || n.Contains("Verified") || n.Contains("NoBackup"));
    }

    internal static string ToolSource(string relativePath) =>
        File.ReadAllText(Path.Combine(ToolDirectory(), relativePath));

    internal static string ToolDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
            dir = dir.Parent;

        dir.Should().NotBeNull("MMV.sln doit être trouvable en remontant depuis le dossier de sortie");
        return Path.Combine(dir!.FullName, "src", "MMV.DatabaseManager");
    }
}
