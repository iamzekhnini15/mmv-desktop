using System.Linq;
using FluentAssertions;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-6 — Garde-fou d'architecture : le workflow des commandes doit posséder <b>une seule</b> matrice de
/// transitions dans le code vivant (backend). Vérifie que :
/// <list type="bullet">
///   <item><see cref="OrderStatusPolicy"/> existe et est l'unique politique de transitions ;</item>
///   <item>l'ancien service mort <c>OrderService</c> / <c>IOrderService</c> (qui portait une SECONDE matrice
///         codée en dur) a bien été supprimé de l'assembly Domain (P3-6).</item>
/// </list>
/// Verrouille l'invariant « source de vérité unique » contre toute réintroduction future.
/// </summary>
public sealed class OrderWorkflowArchitectureTests
{
    [Fact]
    public void OrderStatusPolicy_Exists_InDomainAssembly()
        => typeof(OrderStatusPolicy).Assembly
            .GetTypes()
            .Should().Contain(t => t.Name == nameof(OrderStatusPolicy),
                "la matrice de transitions vit dans la politique Domain unique");

    [Fact]
    public void LegacyOrderService_IsRemoved_FromDomainAssembly()
        => typeof(OrderStatusPolicy).Assembly
            .GetTypes()
            .Select(t => t.Name)
            .Should().NotContain(new[] { "OrderService", "IOrderService" },
                "l'ancienne seconde matrice morte a été supprimée en P3-6 (source de vérité unique)");
}
