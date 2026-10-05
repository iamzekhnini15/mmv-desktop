using System.Text.RegularExpressions;
using FluentAssertions;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.CommandLine;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// P4-6B (H12, R-1 ; §6.2 du plan), P4-9 — aucun contournement de la vérification de sauvegarde. La seule
/// implémentation de production est <see cref="ProofBackupVerification"/> ; <c>Program.cs</c> ne construit
/// qu'elle, avec l'âge maximal de <see cref="BackupPolicy"/> ; ni option, ni variable d'environnement, ni
/// compilation conditionnelle ne l'évitent.
/// </summary>
public sealed class BackupSeamTests
{
    [Theory]
    [InlineData("dump-2026-10-04")]
    [InlineData("verified")]
    [InlineData("relative/path.manifest.json")]
    public async Task A_reference_that_is_not_an_absolute_manifest_path_is_refused(string reference)
    {
        var result = await new ProofBackupVerification(BackupPolicy.MaximumAgeBeforeMigration).VerifyAsync(reference, CancellationToken.None);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("chemin absolu");
    }

    [Fact]
    public void Proof_verification_is_the_only_implementation_in_the_tool()
    {
        typeof(ProofBackupVerification).Assembly.GetTypes()
            .Where(t => typeof(IBackupVerification).IsAssignableFrom(t) && !t.IsInterface)
            .Should().Equal(typeof(ProofBackupVerification));
    }

    [Fact]
    public void Program_constructs_only_the_proof_verification_with_the_policy_age()
    {
        var program = ToolSource("Program.cs");

        Regex.Matches(program, @"new\s+ProofBackupVerification\s*\(\s*BackupPolicy\.MaximumAgeBeforeMigration\s*\)")
            .Should().HaveCount(1);
        Regex.Matches(program, @"new\s+\w*BackupVerification\s*\(").Should().HaveCount(1);
    }

    [Fact]
    public void The_maximum_age_is_a_positive_constant_and_never_an_option()
    {
        BackupPolicy.MaximumAgeBeforeMigration.Should().BePositive();
        ToolSource(Path.Combine("CommandLine", "MigrationToolOptions.cs")).Should()
            .NotMatchRegex(@"""--[a-z-]*(age|old|stale|skip|force)", "aucune option ne relâche la vérification ni l'âge");
        var act = () => new ProofBackupVerification(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_verification_result_without_manifest_is_never_confirmed_by_the_production_verifier()
    {
        var verifier = new ProofBackupVerification(BackupPolicy.MaximumAgeBeforeMigration);
        var live = new DatabaseFingerprint("1", "mmv", 1, DateTimeOffset.UnixEpoch, [], []);

        verifier.ConfirmCurrent(BackupVerificationResult.Verified(), live).IsVerified.Should().BeFalse();
        verifier.ConfirmCurrent(BackupVerificationResult.Refused("x"), live).IsVerified.Should().BeFalse();
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
            .Should().NotContain(n => n.Contains("Skip") || n.Contains("Force") || n.Contains("Verified") || n.Contains("NoBackup")
                                      || n.Contains("Age"));
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
