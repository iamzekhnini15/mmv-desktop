using System.Reflection;
using FluentAssertions;
using MMV.Domain.Entities;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-7 — Garde-fous d'architecture du domaine Ventes, sur le modèle de <c>OrderWorkflowArchitectureTests</c>
/// (P3-6). Verrouille contre toute régression future les invariants structurels établis par P3-7.
/// </summary>
public sealed class SalesBusinessRulesArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(SalePricingPolicy).Assembly;

    // ------------------------------------------------------------------
    // 1. Le service mort a disparu
    // ------------------------------------------------------------------

    [Fact]
    public void SaleService_EtSonInterface_NExistentPlus()
    {
        // SaleService / ISaleService portaient une SECONDE formule monétaire, jamais exécutée : des tests verts
        // sur du code mort donnaient une assurance fausse. Leur réintroduction recréerait la divergence.
        DomainAssembly.GetTypes().Select(t => t.Name)
            .Should().NotContain("SaleService").And.NotContain("ISaleService");
    }

    // ------------------------------------------------------------------
    // 2. Une seule politique monétaire
    // ------------------------------------------------------------------

    [Fact]
    public void UneSeulePolitiqueMonetaireDeVente_ExisteDansLeDomaine()
    {
        var pricingOwners = DomainAssembly.GetTypes()
            .Where(t => t.Name.Contains("Sale", StringComparison.Ordinal)
                        && (t.Name.Contains("Pricing", StringComparison.Ordinal)
                            || t.Name.Contains("Calculator", StringComparison.Ordinal)))
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Any(m => m.ReturnType == typeof(SalePricing)))
            .ToList();

        pricingOwners.Should().ContainSingle("le calcul monétaire d'une vente a un propriétaire UNIQUE")
            .Which.Should().Be(typeof(SalePricingPolicy));
    }

    [Fact]
    public void LEntiteVente_NePorteAucuneRegleDeCalcul()
    {
        // Sale reste une entité de données ; le calcul vit dans la politique, jamais recopié ailleurs.
        typeof(Sale).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // 3. Pureté des couches
    // ------------------------------------------------------------------

    [Fact]
    public void LeDomaine_NeReferenceNiEfCoreNiSqlite()
    {
        var referenced = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

        referenced.Should().NotContain(n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        referenced.Should().NotContain(n => n.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal));
    }

    [Fact]
    public void LesPolitiquesDeVente_SontPures_SansDependanceDePersistance()
    {
        // Une politique pure ne prend aucun dépôt : elle est appelable sans base, donc testable sans base.
        foreach (var policy in new[] { typeof(SalePricingPolicy), typeof(SaleLineOpticsPolicy) })
        {
            policy.Should().BeStatic("une politique pure n'a ni état ni dépendance injectée");

            policy.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters())
                .Should().NotContain(
                    p => p.ParameterType.Name.StartsWith("I", StringComparison.Ordinal)
                         && (p.ParameterType.Name.Contains("Repository", StringComparison.Ordinal)
                             || p.ParameterType.Name.Contains("UnitOfWork", StringComparison.Ordinal)),
                    $"{policy.Name} ne doit dépendre d'aucune persistance");
        }
    }
}
