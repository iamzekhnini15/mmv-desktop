using FluentAssertions;
using MMV.Infrastructure.Configuration;
using Xunit;

namespace MMV.Domain.Tests.Configuration;

/// <summary>
/// P2A-1F — Résolution typée des options de seeding depuis l'environnement.
/// Vérifie le défaut sûr (Production sans seed), le gating explicite du jeu de démonstration,
/// les secrets hors dépôt et le blocage d'une configuration de mot de passe bootstrap invalide.
/// </summary>
public sealed class SeedOptionsResolverTests
{
    private static Dictionary<string, string?> Env(params (string Key, string? Value)[] entries)
    {
        var dictionary = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    [Theory]
    [InlineData(null, ApplicationEnvironment.Production)]
    [InlineData("", ApplicationEnvironment.Production)]
    [InlineData("   ", ApplicationEnvironment.Production)]
    [InlineData("inconnu", ApplicationEnvironment.Production)]   // ambiguë ⇒ défaut sûr
    [InlineData("Production", ApplicationEnvironment.Production)]
    [InlineData("prod", ApplicationEnvironment.Production)]
    [InlineData("Development", ApplicationEnvironment.Development)]
    [InlineData("dev", ApplicationEnvironment.Development)]
    [InlineData("Demonstration", ApplicationEnvironment.Demonstration)]
    [InlineData("demo", ApplicationEnvironment.Demonstration)]
    [InlineData("Test", ApplicationEnvironment.Test)]
    public void ParseEnvironment_MapsAliases_AndFallsBackToProduction(string? value, ApplicationEnvironment expected)
    {
        SeedOptionsResolver.ParseEnvironment(value).Should().Be(expected);
    }

    [Fact]
    public void Resolve_WithNoConfiguration_IsSafeProductionDefault()
    {
        var options = SeedOptionsResolver.Resolve(Env());

        options.Environment.Should().Be(ApplicationEnvironment.Production);
        options.EnableDemoSeed.Should().BeFalse();
        options.ShouldSeedDemoData.Should().BeFalse();
        options.HasBootstrapAdminPassword.Should().BeFalse();
        options.BootstrapAdminUsername.Should().Be("admin");
    }

    [Fact]
    public void Resolve_DemoSeedEnabled_InDevelopment_IsHonored()
    {
        var options = SeedOptionsResolver.Resolve(Env(
            (SeedOptionsResolver.EnvironmentVariableName, "Development"),
            (SeedOptionsResolver.EnableDemoSeedVariableName, "true")));

        options.Environment.Should().Be(ApplicationEnvironment.Development);
        options.EnableDemoSeed.Should().BeTrue();
        options.ShouldSeedDemoData.Should().BeTrue();
    }

    [Fact]
    public void Resolve_DemoSeedEnabled_InProduction_IsNeverApplied()
    {
        // Défense en profondeur : même demandé, le seed de démonstration ne s'applique pas en production.
        var options = SeedOptionsResolver.Resolve(Env(
            (SeedOptionsResolver.EnvironmentVariableName, "Production"),
            (SeedOptionsResolver.EnableDemoSeedVariableName, "true")));

        options.EnableDemoSeed.Should().BeTrue();
        options.ShouldSeedDemoData.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WeakBootstrapPassword_Throws()
    {
        var act = () => SeedOptionsResolver.Resolve(Env(
            (SeedOptionsResolver.BootstrapAdminPasswordVariableName, "weak")));

        act.Should().Throw<SeedConfigurationException>()
            .WithMessage("*bootstrap*");
    }

    [Fact]
    public void Resolve_StrongBootstrapPassword_IsAccepted()
    {
        var options = SeedOptionsResolver.Resolve(Env(
            (SeedOptionsResolver.BootstrapAdminPasswordVariableName, "Str0ngPass"),
            (SeedOptionsResolver.BootstrapAdminUsernameVariableName, "gerant")));

        options.HasBootstrapAdminPassword.Should().BeTrue();
        options.BootstrapAdminPassword.Should().Be("Str0ngPass");
        options.BootstrapAdminUsername.Should().Be("gerant");
    }

    [Fact]
    public void Resolve_BlankBootstrapUsername_FallsBackToDefault()
    {
        var options = SeedOptionsResolver.Resolve(Env(
            (SeedOptionsResolver.BootstrapAdminUsernameVariableName, "   ")));

        options.BootstrapAdminUsername.Should().Be("admin");
    }
}
