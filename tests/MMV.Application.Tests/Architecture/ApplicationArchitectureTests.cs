using System.Reflection;
using FluentAssertions;
using MMV.Application.UseCases.Sales.RegisterSale;
using Xunit;

namespace MMV.Application.Tests.Architecture;

/// <summary>
/// P2B-2C — Test d'architecture différé depuis P2B-2B (garde-fou des frontières de couches,
/// cf. ADR frontières §4.2 et plan de migration §2/§5). Vérifie, par réflexion sur les assemblies
/// <b>réellement référencés</b> par <c>MMV.Application</c>, que la couche Application reste pure :
/// <list type="bullet">
///   <item>elle dépend de <c>MMV.Domain</c> (contrôle positif : le test n'est pas vide) ;</item>
///   <item>elle ne référence ni <c>MMV.Infrastructure</c>, ni <c>MMV.App</c> ;</item>
///   <item>elle ne référence ni Avalonia, ni EF Core, ni Microsoft.Data.Sqlite.</item>
/// </list>
/// <c>GetReferencedAssemblies()</c> ne liste que les assemblies effectivement liés en métadonnées :
/// une dépendance interdite y apparaîtrait (et, en pratique, ne compilerait pas, l'invariant étant déjà
/// garanti au niveau du <c>.csproj</c>). Ce test verrouille l'invariant contre toute régression future.
/// </summary>
public sealed class ApplicationArchitectureTests
{
    private static readonly string[] ReferencedAssemblyNames = typeof(RegisterSaleUseCase).Assembly
        .GetReferencedAssemblies()
        .Select(a => a.Name ?? string.Empty)
        .ToArray();

    [Fact]
    public void Application_References_Domain()
        => ReferencedAssemblyNames.Should().Contain("MMV.Domain",
            "la couche Application orchestre des entités/ports du Domain");

    [Fact]
    public void Application_DoesNotReference_Infrastructure()
        => ReferencedAssemblyNames.Should().NotContain(n => n.StartsWith("MMV.Infrastructure"));

    [Fact]
    public void Application_DoesNotReference_App()
        => ReferencedAssemblyNames.Should().NotContain("MMV.App");

    [Fact]
    public void Application_DoesNotReference_Avalonia()
        => ReferencedAssemblyNames.Should().NotContain(n => n.StartsWith("Avalonia"));

    [Fact]
    public void Application_DoesNotReference_EntityFrameworkCore()
        => ReferencedAssemblyNames.Should().NotContain(n => n.StartsWith("Microsoft.EntityFrameworkCore"));

    [Fact]
    public void Application_DoesNotReference_Sqlite()
        => ReferencedAssemblyNames.Should().NotContain(n => n.StartsWith("Microsoft.Data.Sqlite"));
}
