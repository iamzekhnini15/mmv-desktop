using FluentAssertions;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-6 — Politique de workflow des commandes (<see cref="OrderStatusPolicy"/>), <b>unique source de vérité</b>
/// des transitions de statut. Vérifie exhaustivement que :
/// <list type="number">
///   <item>les 5 (et seulement 5) transitions du workflow linéaire strict sont autorisées ;</item>
///   <item>tous les autres couples (sauts, retours arrière, même statut, sortie de Delivered) sont refusés ;</item>
///   <item>sur les 36 couples de l'énumération, exactement 5 sont autorisés ;</item>
///   <item><see cref="OrderStatusPolicy.GetNext"/> est cohérent avec <see cref="OrderStatusPolicy.IsAllowed"/>.</item>
/// </list>
/// Règle pure du Domain : ni base, ni contexte, ni dépendance.
/// </summary>
public sealed class OrderStatusPolicyTests
{
    private static readonly OrderStatus[] AllStatuses =
        (OrderStatus[])System.Enum.GetValues(typeof(OrderStatus));

    // ------------------------------------------------------------------
    // Transitions autorisées (les 5 crans du workflow linéaire strict)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.ToFabricate)]
    [InlineData(OrderStatus.ToFabricate, OrderStatus.InProgress)]
    [InlineData(OrderStatus.InProgress, OrderStatus.QualityCheck)]
    [InlineData(OrderStatus.QualityCheck, OrderStatus.Ready)]
    [InlineData(OrderStatus.Ready, OrderStatus.Delivered)]
    public void IsAllowed_LinearWorkflowStep_ReturnsTrue(OrderStatus current, OrderStatus next)
        => OrderStatusPolicy.IsAllowed(current, next).Should().BeTrue();

    // ------------------------------------------------------------------
    // Refus : sauts d'étape
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.InProgress)]
    [InlineData(OrderStatus.New, OrderStatus.Delivered)]
    [InlineData(OrderStatus.New, OrderStatus.QualityCheck)]
    [InlineData(OrderStatus.New, OrderStatus.Ready)]
    [InlineData(OrderStatus.ToFabricate, OrderStatus.Ready)]
    [InlineData(OrderStatus.ToFabricate, OrderStatus.Delivered)]
    [InlineData(OrderStatus.InProgress, OrderStatus.Delivered)]
    public void IsAllowed_StepSkip_ReturnsFalse(OrderStatus current, OrderStatus next)
        => OrderStatusPolicy.IsAllowed(current, next).Should().BeFalse();

    // ------------------------------------------------------------------
    // Refus : retours arrière
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.InProgress, OrderStatus.New)]
    [InlineData(OrderStatus.InProgress, OrderStatus.ToFabricate)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered, OrderStatus.New)]
    [InlineData(OrderStatus.Ready, OrderStatus.QualityCheck)]
    [InlineData(OrderStatus.ToFabricate, OrderStatus.New)]
    public void IsAllowed_Backward_ReturnsFalse(OrderStatus current, OrderStatus next)
        => OrderStatusPolicy.IsAllowed(current, next).Should().BeFalse();

    // ------------------------------------------------------------------
    // Refus : même statut vers même statut (no-op)
    // ------------------------------------------------------------------

    [Fact]
    public void IsAllowed_SameStatus_AlwaysReturnsFalse()
    {
        foreach (var status in AllStatuses)
            OrderStatusPolicy.IsAllowed(status, status).Should().BeFalse($"{status} → {status} est un no-op interdit");
    }

    // ------------------------------------------------------------------
    // Refus : toute transition depuis Delivered (terminal)
    // ------------------------------------------------------------------

    [Fact]
    public void IsAllowed_FromDelivered_AlwaysReturnsFalse()
    {
        foreach (var next in AllStatuses)
            OrderStatusPolicy.IsAllowed(OrderStatus.Delivered, next).Should().BeFalse("Delivered est terminal");
    }

    // ------------------------------------------------------------------
    // Matrice exhaustive : sur les 36 couples, exactement 5 autorisés
    // ------------------------------------------------------------------

    [Fact]
    public void IsAllowed_OverAllPairs_ExactlyFiveAllowed()
    {
        var allowed = 0;
        foreach (var current in AllStatuses)
        {
            foreach (var next in AllStatuses)
            {
                if (OrderStatusPolicy.IsAllowed(current, next))
                    allowed++;
            }
        }

        AllStatuses.Length.Should().Be(6, "l'énumération compte 6 statuts (36 couples)");
        allowed.Should().Be(5, "seuls les 5 crans du workflow linéaire strict sont autorisés");
    }

    // ------------------------------------------------------------------
    // GetNext : successeur unique + cohérence avec IsAllowed
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.ToFabricate)]
    [InlineData(OrderStatus.ToFabricate, OrderStatus.InProgress)]
    [InlineData(OrderStatus.InProgress, OrderStatus.QualityCheck)]
    [InlineData(OrderStatus.QualityCheck, OrderStatus.Ready)]
    [InlineData(OrderStatus.Ready, OrderStatus.Delivered)]
    public void GetNext_ReturnsUniqueSuccessor(OrderStatus current, OrderStatus expectedNext)
        => OrderStatusPolicy.GetNext(current).Should().Be(expectedNext);

    [Fact]
    public void GetNext_FromDelivered_IsNull()
        => OrderStatusPolicy.GetNext(OrderStatus.Delivered).Should().BeNull("Delivered est terminal");

    [Fact]
    public void IsAllowed_IsConsistentWith_GetNext_OverAllPairs()
    {
        foreach (var current in AllStatuses)
        {
            var next = OrderStatusPolicy.GetNext(current);
            foreach (var candidate in AllStatuses)
            {
                var expected = next is { } n && n == candidate;
                OrderStatusPolicy.IsAllowed(current, candidate).Should().Be(expected,
                    $"IsAllowed({current}, {candidate}) doit être dérivé de GetNext({current})");
            }
        }
    }
}
