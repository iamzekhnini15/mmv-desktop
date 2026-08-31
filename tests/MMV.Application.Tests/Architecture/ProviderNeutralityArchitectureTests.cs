using FluentAssertions;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Exceptions;
using Xunit;

namespace MMV.Application.Tests.Architecture;

/// <summary>
/// P4-4A1 — Garde O14 : <b>neutralité vis-à-vis du fournisseur de base de données</b>.
///
/// <para>
/// P4-3 a introduit <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> et P4-4A1 fait entrer les types
/// <c>Npgsql</c> dans <c>PersistenceErrorMapper</c>. Ces types doivent rester CONFINÉS à
/// <c>MMV.Infrastructure</c> : <c>MMV.Domain</c> et <c>MMV.Application</c> raisonnent uniquement sur
/// <see cref="PersistenceException"/> / <see cref="PersistenceErrorCategory"/> et ne doivent
/// connaître aucun fournisseur.
/// </para>
///
/// <para>
/// Vérification par réflexion sur les assemblies <b>réellement référencés</b>
/// (<c>GetReferencedAssemblies()</c> ne liste que les références liées en métadonnées). Ce test
/// échoue si quelqu'un ajoute plus tard une dépendance Npgsql dans l'une des deux couches — la garde
/// couvre explicitement les DEUX assemblies. Complète les gardes existantes
/// (<c>ApplicationArchitectureTests</c>) sans les dupliquer : celles-ci ne couvrent ni Npgsql, ni
/// l'assembly Domain.
/// </para>
/// </summary>
public sealed class ProviderNeutralityArchitectureTests
{
    private static readonly string[] DomainReferences = typeof(PersistenceException).Assembly
        .GetReferencedAssemblies()
        .Select(a => a.Name ?? string.Empty)
        .ToArray();

    private static readonly string[] ApplicationReferences = typeof(RegisterSaleUseCase).Assembly
        .GetReferencedAssemblies()
        .Select(a => a.Name ?? string.Empty)
        .ToArray();

    /// <summary>
    /// Le nom d'assembly désigne le fournisseur PostgreSQL : « Npgsql » exactement (le pilote ADO.NET)
    /// ou tout assembly préfixé « Npgsql. » (par ex. <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>).
    /// </summary>
    private static bool IsNpgsqlAssembly(string name)
        => string.Equals(name, "Npgsql", StringComparison.Ordinal)
           || name.StartsWith("Npgsql.", StringComparison.Ordinal);

    /// <summary>
    /// Contrôle positif : sans lui, un <c>GetReferencedAssemblies()</c> vide rendrait les deux gardes
    /// ci-dessous vraies pour une mauvaise raison.
    /// </summary>
    [Fact]
    public void ReferenceListsAreNotEmpty()
    {
        DomainReferences.Should().NotBeEmpty();
        ApplicationReferences.Should().Contain("MMV.Domain",
            "la couche Application orchestre des entités/ports du Domain");
    }

    [Fact]
    public void Domain_DoesNotReference_NpgsqlProvider()
        => DomainReferences.Should().NotContain(name => IsNpgsqlAssembly(name),
            "le fournisseur PostgreSQL reste confiné à MMV.Infrastructure (O14)");

    [Fact]
    public void Application_DoesNotReference_NpgsqlProvider()
        => ApplicationReferences.Should().NotContain(name => IsNpgsqlAssembly(name),
            "le fournisseur PostgreSQL reste confiné à MMV.Infrastructure (O14)");
}
