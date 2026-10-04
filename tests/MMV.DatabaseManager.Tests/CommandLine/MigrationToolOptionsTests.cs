using FluentAssertions;
using MMV.DatabaseManager.CommandLine;

namespace MMV.DatabaseManager.Tests.CommandLine;

/// <summary>
/// P4-6B (C4) — analyse des arguments, sans défaut permissif. U-C1 (<c>--operator</c>), U-C7 (<c>--app-role</c>),
/// U-C9 (aucune option de contournement de la sauvegarde).
/// </summary>
public sealed class MigrationToolOptionsTests
{
    private static MigrationToolParseResult Parse(params string[] args) => MigrationToolOptions.Parse(args);

    [Fact]
    public void Complete_migrate_is_valid()
    {
        var result = Parse("migrate", "--operator", "OP-42", "--backup-ref", "dump-1", "--app-role", "mmv_app", "--wait", "30");

        result.IsValid.Should().BeTrue();
        var options = result.Options!;
        options.Verb.Should().Be(MigrationVerb.Migrate);
        options.Operator.Should().Be("OP-42");
        options.BackupReference.Should().Be("dump-1");
        options.AppRole.Should().Be("mmv_app");
        options.Wait.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Wait_has_a_short_bounded_default()
    {
        var options = Parse("migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", "r").Options!;

        options.Wait.Should().Be(MigrationToolOptions.DefaultWait);
        MigrationToolOptions.DefaultWait.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("3601")]
    public void Wait_is_bounded_and_never_infinite(string wait)
    {
        Parse("migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", "r", "--wait", wait)
            .IsValid.Should().BeFalse();
    }

    // ---- U-C1 ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("migrate")]
    [InlineData("adopt-compatibility")]
    public void Operator_is_mandatory(string verb)
    {
        var result = Parse(verb, "--backup-ref", "b", "--app-role", "r");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("--operator");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_operator_is_refused(string value)
    {
        Parse("migrate", "--operator", value, "--backup-ref", "b", "--app-role", "r").IsValid.Should().BeFalse();
    }

    // ---- U-C7 ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("migrate")]
    [InlineData("adopt-compatibility")]
    public void App_role_is_mandatory_for_writing_verbs(string verb)
    {
        var result = Parse(verb, "--operator", "OP", "--backup-ref", "b");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("--app-role");
    }

    [Fact]
    public void Status_requires_neither_app_role_nor_operator()
    {
        var result = Parse("status");

        result.IsValid.Should().BeTrue();
        result.Options!.Verb.Should().Be(MigrationVerb.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("role\0nul")]
    [InlineData("role\nnewline")]
    [InlineData("a_role_name_that_is_far_too_long_for_a_postgresql_identifier_max")]
    public void Unusable_app_role_is_refused(string role)
    {
        Parse("migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", role).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("x; DROP TABLE \"Customers\"; --")]
    [InlineData("rôle \"cité\"")]
    [InlineData("Mixed Case")]
    public void Hostile_but_valid_identifiers_are_kept_verbatim_for_server_side_quoting(string role)
    {
        var result = Parse("migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", role);

        result.IsValid.Should().BeTrue();
        result.Options!.AppRole.Should().Be(role, "la valeur n'est jamais réécrite côté client : le serveur la cite (%I)");
    }

    // ---- verbes et options inconnus --------------------------------------------------------------------

    [Theory]
    [InlineData]
    [InlineData("rollback")]
    [InlineData("drop")]
    [InlineData("down")]
    [InlineData("--operator", "OP")]
    public void Unknown_or_missing_verb_is_refused(params string[] args)
    {
        Parse(args).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("--skip-backup")]
    [InlineData("--no-backup")]
    [InlineData("--force")]
    [InlineData("--backup-verified")]
    public void No_option_bypasses_the_backup_verification(string option)
    {
        Parse("migrate", "--operator", "OP", "--backup-ref", "b", "--app-role", "r", option).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Duplicate_option_is_refused()
    {
        Parse("migrate", "--operator", "A", "--operator", "B", "--backup-ref", "b", "--app-role", "r")
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Option_without_value_is_refused()
    {
        Parse("migrate", "--backup-ref", "b", "--app-role", "r", "--operator").IsValid.Should().BeFalse();
    }

    [Fact]
    public void Missing_backup_reference_parses_and_is_refused_later_with_code_11()
    {
        // H12 : l'absence de référence est un refus de sauvegarde (code 11), pas une erreur d'argument.
        var result = Parse("migrate", "--operator", "OP", "--app-role", "r");

        result.IsValid.Should().BeTrue();
        result.Options!.BackupReference.Should().BeNull();
    }
}
