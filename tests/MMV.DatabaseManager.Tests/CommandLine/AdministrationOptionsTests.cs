using FluentAssertions;
using MMV.DatabaseManager.CommandLine;

namespace MMV.DatabaseManager.Tests.CommandLine;

/// <summary>
/// P4-8 — options des verbes d'administration : aucun défaut permissif, aucune option de secret, aucune option qui
/// contourne une garde ; mêmes refus que les verbes P4-6B.
/// </summary>
public sealed class AdministrationOptionsTests
{
    private static readonly string[] Provision =
    [
        "provision", "--host", "srv.lan", "--admin-user", "postgres", "--database", "mmv",
        "--migrator-role", "m", "--app-role", "a", "--backup-role", "b", "--operator", "OP"
    ];

    [Fact]
    public void Complete_provision_is_valid_with_safe_defaults()
    {
        var parsed = AdministrationOptions.Parse(Provision);

        parsed.IsValid.Should().BeTrue(parsed.Error);
        parsed.Options!.Verb.Should().Be(AdministrationVerb.Provision);
        parsed.Options.Port.Should().Be(5432);
        parsed.Options.AdminDatabase.Should().Be("postgres");
        parsed.Options.Get("--root-certificate").Should().BeNull();
    }

    [Theory]
    [InlineData("rotate-role-password", "--host", "h", "--admin-user", "u", "--database", "d", "--role", "r", "--operator", "OP")]
    [InlineData("bootstrap-admin", "--username", "patron", "--operator", "OP")]
    [InlineData("configure-workstation", "--host", "h", "--database", "d", "--username", "r", "--port", "5433")]
    public void Every_verb_parses_its_complete_form(params string[] args)
    {
        AdministrationOptions.Parse(args).IsValid.Should().BeTrue();
        AdministrationOptions.IsAdministrationVerb(args).Should().BeTrue();
    }

    [Theory]
    [InlineData("--host")]
    [InlineData("--admin-user")]
    [InlineData("--database")]
    [InlineData("--migrator-role")]
    [InlineData("--app-role")]
    [InlineData("--backup-role")]
    [InlineData("--operator")]
    public void Every_required_provision_option_is_mandatory(string option)
    {
        var index = Array.IndexOf(Provision, option);
        var args = Provision.Where((_, i) => i != index && i != index + 1).ToArray();

        AdministrationOptions.Parse(args).Error.Should().Contain(option);
    }

    [Theory]
    [InlineData("--password", "x")]
    [InlineData("--admin-password", "x")]
    [InlineData("--secret", "x")]
    [InlineData("--force", "x")]
    [InlineData("--skip-tls", "x")]
    [InlineData("--ssl-mode", "Disable")]
    public void No_option_carries_a_secret_or_weakens_a_guard(string option, string value)
    {
        AdministrationOptions.Parse([.. Provision, option, value]).Error.Should().Contain("inconnue");
    }

    [Theory]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "abc")]
    [InlineData("--root-certificate", "relative/ca.crt")]
    [InlineData("--admin-database", "a_database_name_that_is_far_too_long_for_a_postgresql_identifier")]
    public void Out_of_range_or_unsafe_values_are_refused(string option, string value)
    {
        AdministrationOptions.Parse([.. Provision, option, value]).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("--app-role", "")]
    [InlineData("--app-role", "a\nb")]
    [InlineData("--operator", " ")]
    public void Empty_or_control_values_are_refused(string option, string value)
    {
        var args = Provision.ToArray();
        args[Array.IndexOf(args, option) + 1] = value;

        AdministrationOptions.Parse(args).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Repeated_or_valueless_options_are_refused()
    {
        AdministrationOptions.Parse([.. Provision, "--host", "other"]).Error.Should().Contain("répétée");
        AdministrationOptions.Parse([.. Provision, "--port"]).Error.Should().Contain("attend une valeur");
    }

    [Fact]
    public void Options_of_another_verb_are_refused()
    {
        AdministrationOptions.Parse(["bootstrap-admin", "--username", "p", "--operator", "OP", "--host", "h"])
            .Error.Should().Contain("inconnue");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("migrate")]
    [InlineData("drop")]
    public void Migration_and_unknown_verbs_are_not_administration_verbs(string verb)
    {
        AdministrationOptions.IsAdministrationVerb([verb]).Should().BeFalse();
    }
}
