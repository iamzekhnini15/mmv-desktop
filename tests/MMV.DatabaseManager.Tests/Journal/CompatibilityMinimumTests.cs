using System.Reflection;
using FluentAssertions;
using MMV.DatabaseManager.CommandLine;
using MMV.DatabaseManager.Journal;

namespace MMV.DatabaseManager.Tests.Journal;

/// <summary>
/// P4-6B — minimum supporté <b>calculé</b> depuis le journal, jamais saisi (H17, DP-3.5.3 ; §4.3 du plan).
/// U-C3 (calcul sur journaux injectés), U-C4 (aucune surface publique ne permet de fixer le minimum).
/// </summary>
public sealed class CompatibilityMinimumTests
{
    private static readonly string[] S1 = ["B"];
    private static readonly string[] S2 = ["B", "M1"];
    private static readonly string[] S3 = ["B", "M1", "M2"];

    private static MigrationRunRecord Migrate(string version, string[] before, string[] after,
        MigrationOutcome outcome = MigrationOutcome.Success) =>
        new(version, MigrationRunKind.Migrate, outcome, before, after);

    private static MigrationRunRecord Adopt(string version, string[] applied) =>
        new(version, MigrationRunKind.Adopt, MigrationOutcome.Success, applied, applied);

    // ---- U-C3 : l'exemple de référence du §4.3, ligne par ligne ----------------------------------------

    [Fact]
    public void Reference_example_gives_the_exact_values_of_the_table()
    {
        var r1 = Migrate("1.0.0", [], S1);
        var r2 = Migrate("1.1.0", S1, S2);
        var r3 = Migrate("1.2.0", S2, S2); // release SANS migration
        var r4 = Migrate("1.3.0", S2, S3);

        CompatibilityMetadataWriter.Compute([r1]).Should().BeEquivalentTo(new { SchemaVersion = "1.0.0", MinimumSupportedVersion = "1.0.0" });
        CompatibilityMetadataWriter.Compute([r1, r2]).Should().BeEquivalentTo(new { SchemaVersion = "1.1.0", MinimumSupportedVersion = "1.0.0" });
        CompatibilityMetadataWriter.Compute([r1, r2, r3]).Should().BeNull("aucune écriture : la ligne reste (1.1.0, 1.0.0)");
        CompatibilityMetadataWriter.Compute([r1, r2, r3, r4]).Should().BeEquivalentTo(new { SchemaVersion = "1.3.0", MinimumSupportedVersion = "1.1.0" });
    }

    [Fact]
    public void A_release_without_migration_consumes_no_step()
    {
        CompatibilityMetadataWriter.Compute(
            [Migrate("1.0.0", [], S1), Migrate("1.1.0", S1, S2), Migrate("1.2.0", S2, S2), Migrate("1.2.1", S2, S2)])
            .Should().BeNull();

        // Les releases sans migration ne s'insèrent jamais dans la suite des versions productrices.
        CompatibilityMetadataWriter.Compute(
            [Migrate("1.0.0", [], S1), Migrate("1.1.0", S1, S2), Migrate("1.2.0", S2, S2), Migrate("1.3.0", S2, S3)])
            .Should().BeEquivalentTo(new { SchemaVersion = "1.3.0", MinimumSupportedVersion = "1.1.0" });
    }

    [Fact]
    public void An_unverified_state_is_never_an_anchor()
    {
        // L'exécution courante (dernière de la liste) n'est pas un succès : rien n'est écrit.
        CompatibilityMetadataWriter.Compute(
            [Migrate("1.0.0", [], S1), Migrate("1.1.0", S1, S2, MigrationOutcome.Failure)]).Should().BeNull();
        CompatibilityMetadataWriter.Compute(
            [Migrate("1.0.0", [], S1), Migrate("1.1.0", S1, S2, MigrationOutcome.Open)]).Should().BeNull();
    }

    [Fact]
    public void A_failure_that_applied_nothing_does_not_change_the_attribution()
    {
        var versions = CompatibilityMetadataWriter.Compute(
        [
            Migrate("1.0.0", [], S1),
            Migrate("1.1.0", S1, S2),
            Migrate("1.3.0", S2, S2, MigrationOutcome.Failure),
            Migrate("1.3.0", S2, S3)
        ]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.3.0", MinimumSupportedVersion = "1.1.0" });
    }

    // ---- Amendement d'architecte 04/10/2026 : ancre = état physique vérifié ---------------------------

    [Fact]
    public void Scenario1_first_install_failing_at_step_9_then_relaunch_anchors_the_verified_state()
    {
        var versions = CompatibilityMetadataWriter.Compute(
        [
            Migrate("1.0.0", [], S1, MigrationOutcome.Failure), // baseline appliquée, vérification en échec
            Migrate("1.0.0", S1, S1)                            // relance : rien à migrer, état vérifié
        ]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.0.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public void Scenario2_migration_applied_then_verification_failed_then_relaunch_anchors_its_producer()
    {
        var versions = CompatibilityMetadataWriter.Compute(
        [
            Migrate("1.0.0", [], S1),
            Migrate("1.1.0", S1, S2, MigrationOutcome.Failure),
            Migrate("1.1.0", S2, S2)
        ]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.1.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public void Scenario3_relaunch_by_a_newer_release_without_migration_keeps_the_producer_version()
    {
        var versions = CompatibilityMetadataWriter.Compute(
        [
            Migrate("1.0.0", [], S1),
            Migrate("1.1.0", S1, S2, MigrationOutcome.Failure),
            Migrate("1.2.0", S2, S2) // binaire plus récent, aucune migration nouvelle
        ]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.1.0", MinimumSupportedVersion = "1.0.0" },
            "schema_version est la release qui a produit le schéma physique, jamais la version courante");
    }

    [Fact]
    public void Scenario4_a_release_without_migration_after_an_anchor_writes_nothing()
    {
        CompatibilityMetadataWriter.Compute(
            [Migrate("1.0.0", [], S1), Migrate("1.1.0", S1, S2), Migrate("1.2.0", S2, S2)]).Should().BeNull();
    }

    [Fact]
    public void Scenario5_failure_then_new_release_keeps_N_minus_2_out_of_the_window()
    {
        string[] s3 = ["B", "M1", "M2"];
        string[] s4 = ["B", "M1", "M2", "M3"];

        var versions = CompatibilityMetadataWriter.Compute(
        [
            Migrate("1.0.0", [], S1),
            Migrate("1.1.0", S1, S2),                            // N = 1.1.0
            Migrate("1.2.0", S2, s3, MigrationOutcome.Failure),  // M2 appliquée, vérification en échec
            Migrate("1.3.0", s3, s4)                             // nouvelle release
        ]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.3.0", MinimumSupportedVersion = "1.2.0" },
            "le schéma immédiatement précédent est celui que 1.2.0 a physiquement produit : 1.1.0 (N-2) reste bloqué");
    }

    [Fact]
    public void A_crashed_run_is_still_the_producer_of_what_it_applied()
    {
        var versions = CompatibilityMetadataWriter.Compute(
        [
            Migrate("1.0.0", [], S1),
            new MigrationRunRecord("1.1.0", MigrationRunKind.Migrate, MigrationOutcome.Open, S1, null), // tuée
            Migrate("1.2.0", S2, S2)
        ]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.1.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public void A_migration_applied_outside_any_journaled_run_is_refused()
    {
        var act = () => CompatibilityMetadataWriter.Compute([Migrate("1.0.0", S1, S1)]);

        act.Should().Throw<InvalidOperationException>("la provenance d'une migration n'est jamais devinée");
    }

    [Fact]
    public void An_adopt_run_is_an_anchor()
    {
        CompatibilityMetadataWriter.Compute([Adopt("1.0.0", S1)]).Should().BeEquivalentTo(new { SchemaVersion = "1.0.0", MinimumSupportedVersion = "1.0.0" });
        CompatibilityMetadataWriter.Compute([Adopt("1.0.0", S1), Migrate("1.1.0", S1, S2)])
            .Should().BeEquivalentTo(new { SchemaVersion = "1.1.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public void An_adopt_run_is_a_new_starting_point()
    {
        // L'adoption pose la valeur la plus stricte (§4.4) : rien d'antérieur ne rouvre une fenêtre.
        CompatibilityMetadataWriter.Compute([Migrate("0.9.0", [], S1), Adopt("1.0.0", S1)])
            .Should().BeEquivalentTo(new { SchemaVersion = "1.0.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public void Consecutive_identical_versions_are_merged()
    {
        var versions = CompatibilityMetadataWriter.Compute(
            [Migrate("1.0.0", [], S1), Migrate("1.1.0", S1, S2), Migrate("1.1.0", S2, S3)]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.1.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public void A_journal_without_verified_state_writes_nothing()
    {
        CompatibilityMetadataWriter.Compute([]).Should().BeNull();
        CompatibilityMetadataWriter.Compute([Migrate("1.0.0", [], S1, MigrationOutcome.Failure)]).Should().BeNull();
    }

    [Fact]
    public void Versions_are_ordered_by_journal_not_by_value()
    {
        // Aucune valeur codée en dur, aucun décalage : seul l'ordre du journal fait foi.
        var versions = CompatibilityMetadataWriter.Compute([Migrate("2.0.0", [], S1), Migrate("1.5.0", S1, S2)]);

        versions.Should().BeEquivalentTo(new { SchemaVersion = "1.5.0", MinimumSupportedVersion = "2.0.0" });
    }

    // ---- U-C4 : le minimum n'est jamais un paramètre ---------------------------------------------------

    [Fact]
    public void No_public_surface_accepts_a_minimum()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var offenders = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                         .Cast<MethodBase>()
                         .Concat(type.GetConstructors()))
            {
                offenders.AddRange(method.GetParameters()
                    .Where(p => p.Name!.Contains("minimum", StringComparison.OrdinalIgnoreCase))
                    .Select(p => $"{type.Name}.{method.Name}({p.Name})"));
            }

            offenders.AddRange(type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(p => p.Name.Contains("Minimum", StringComparison.OrdinalIgnoreCase) && p.SetMethod?.IsPublic == true)
                .Select(p => $"{type.Name}.{p.Name} (set)"));
        }

        offenders.Should().BeEmpty("le minimum supporté est un constat calculé depuis le journal, jamais une saisie (H17)");
    }

    [Theory]
    [InlineData("--minimum-supported-version")]
    [InlineData("--minimum")]
    [InlineData("--schema-version")]
    public void No_command_line_option_sets_a_version(string option)
    {
        var result = MigrationToolOptions.Parse(
            ["migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", "r", option, "1.0.0"]);

        result.IsValid.Should().BeFalse();
    }
}
