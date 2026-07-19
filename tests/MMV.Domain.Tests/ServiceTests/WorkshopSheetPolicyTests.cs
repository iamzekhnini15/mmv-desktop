using FluentAssertions;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-6B — <see cref="WorkshopSheetPolicy"/> : règles pures de génération et de contrôle qualité d'une fiche.
/// </summary>
public sealed class WorkshopSheetPolicyTests
{
    [Theory]
    [InlineData(OrderStatus.InProgress)]
    [InlineData(OrderStatus.QualityCheck)]
    public void CanGenerateForStatus_AutoriseLesEtapesDAtelier(OrderStatus status)
        => WorkshopSheetPolicy.CanGenerateForStatus(status).Should().BeTrue();

    [Theory]
    [InlineData(OrderStatus.New)]
    [InlineData(OrderStatus.ToFabricate)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Delivered)]
    public void CanGenerateForStatus_RefuseHorsAtelier(OrderStatus status)
        => WorkshopSheetPolicy.CanGenerateForStatus(status).Should().BeFalse();

    [Fact]
    public void CanGenerateForStatus_CouvreExactementDeuxStatutsSurSix()
    {
        var allowed = Enum.GetValues<OrderStatus>().Count(WorkshopSheetPolicy.CanGenerateForStatus);

        allowed.Should().Be(2);
    }

    [Fact]
    public void CanTakeQcDecision_UniquementDepuisPending()
    {
        WorkshopSheetPolicy.CanTakeQcDecision(WorkshopSheetQcStatus.Pending).Should().BeTrue();
        WorkshopSheetPolicy.CanTakeQcDecision(WorkshopSheetQcStatus.Passed).Should().BeFalse();
        WorkshopSheetPolicy.CanTakeQcDecision(WorkshopSheetQcStatus.Failed).Should().BeFalse();
    }

    [Fact]
    public void IsTerminalQcDecision_SeulesPassedEtFailedSontDesDecisions()
    {
        WorkshopSheetPolicy.IsTerminalQcDecision(WorkshopSheetQcStatus.Passed).Should().BeTrue();
        WorkshopSheetPolicy.IsTerminalQcDecision(WorkshopSheetQcStatus.Failed).Should().BeTrue();
        WorkshopSheetPolicy.IsTerminalQcDecision(WorkshopSheetQcStatus.Pending).Should().BeFalse();
    }

    [Fact]
    public void RequiresComment_SeulLeRefusDoitEtreMotive()
    {
        WorkshopSheetPolicy.RequiresComment(WorkshopSheetQcStatus.Failed).Should().BeTrue();
        WorkshopSheetPolicy.RequiresComment(WorkshopSheetQcStatus.Passed).Should().BeFalse();
    }

    [Fact]
    public void QcStatus_NeComporteQueTroisEtats()
    {
        Enum.GetValues<WorkshopSheetQcStatus>().Should().BeEquivalentTo(new[]
        {
            WorkshopSheetQcStatus.Pending,
            WorkshopSheetQcStatus.Passed,
            WorkshopSheetQcStatus.Failed
        });
    }
}
