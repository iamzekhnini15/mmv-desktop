using FluentAssertions;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-7 — <see cref="SalePricingPolicy"/>, <b>unique propriétaire</b> du calcul monétaire d'une création de vente.
/// Remplace la valeur de couverture des anciens <c>SaleServiceTests</c>, qui exerçaient une seconde formule
/// monétaire <b>morte</b> (aucun consommateur runtime) et donnaient donc une assurance fausse.
/// </summary>
/// <remarks>
/// Aucun total n'est fixé à l'avance : chaque attendu est exprimé à partir des entrées du test.
/// </remarks>
public sealed class SalePricingPolicyTests
{
    private static SaleLinePricingInput Line(int quantity, decimal unitPrice) => new(quantity, unitPrice);

    // ------------------------------------------------------------------
    // Lignes
    // ------------------------------------------------------------------

    [Fact]
    public void UneLigne_CalculeLeTotalDeLigne()
    {
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 120m) }, discountAmount: 0m, depositAmount: 0m);

        pricing.Lines.Should().ContainSingle();
        pricing.Lines[0].LineTotal.Should().Be(1 * 120m);
        pricing.TotalAmount.Should().Be(1 * 120m);
    }

    [Fact]
    public void PlusieursLignes_SommeLesTotauxDeLigne()
    {
        var pricing = SalePricingPolicy.Calculate(
            new[] { Line(1, 120m), Line(3, 15.50m), Line(2, 40m) },
            discountAmount: 0m,
            depositAmount: 0m);

        pricing.Lines.Select(l => l.LineTotal).Should().Equal(1 * 120m, 3 * 15.50m, 2 * 40m);
        pricing.TotalAmount.Should().Be((1 * 120m) + (3 * 15.50m) + (2 * 40m));
    }

    [Fact]
    public void QuantiteSuperieureAUn_MultiplieLePrixUnitaire()
    {
        var pricing = SalePricingPolicy.Calculate(new[] { Line(7, 12.30m) }, 0m, 0m);

        pricing.Lines[0].LineTotal.Should().Be(7 * 12.30m);
    }

    [Fact]
    public void PrixZero_EstAccepte()
    {
        // Gratuité : geste commercial légitime. La borne est « ≥ 0 », jamais « > 0 ».
        var pricing = SalePricingPolicy.Calculate(new[] { Line(2, 0m) }, 0m, 0m);

        pricing.TotalAmount.Should().Be(0m);
        pricing.FinalAmount.Should().Be(0m);
    }

    [Fact]
    public void CollectionVide_EstRefusee()
    {
        var act = () => SalePricingPolicy.Calculate(Array.Empty<SaleLinePricingInput>(), 0m, 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.EmptyLinesMessage);
    }

    [Fact]
    public void CollectionNulle_EstRefusee()
    {
        var act = () => SalePricingPolicy.Calculate(null, 0m, 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.EmptyLinesMessage);
    }

    [Fact]
    public void QuantiteZero_EstRefusee()
    {
        var act = () => SalePricingPolicy.Calculate(new[] { Line(0, 10m) }, 0m, 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.QuantityMessage);
    }

    [Fact]
    public void QuantiteNegative_EstRefusee()
    {
        var act = () => SalePricingPolicy.Calculate(new[] { Line(-2, 10m) }, 0m, 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.QuantityMessage);
    }

    [Fact]
    public void PrixNegatif_EstRefuse()
    {
        var act = () => SalePricingPolicy.Calculate(new[] { Line(1, -0.01m) }, 0m, 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.UnitPriceMessage);
    }

    // ------------------------------------------------------------------
    // Remise
    // ------------------------------------------------------------------

    [Fact]
    public void RemiseNegative_EstRefusee()
    {
        var act = () => SalePricingPolicy.Calculate(new[] { Line(1, 100m) }, discountAmount: -1m, depositAmount: 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.DiscountNegativeMessage);
    }

    [Fact]
    public void RemiseEgaleAuTotal_EstAcceptee()
    {
        var total = 1 * 100m;
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 100m) }, discountAmount: total, depositAmount: 0m);

        pricing.FinalAmount.Should().Be(0m, "remise intégrale : rien ne reste dû");
        pricing.RemainingAmount.Should().Be(0m);
    }

    [Fact]
    public void RemiseSuperieureAuTotal_EstRefusee()
    {
        var total = 1 * 100m;
        var act = () => SalePricingPolicy.Calculate(new[] { Line(1, 100m) }, discountAmount: total + 0.01m, depositAmount: 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.DiscountAboveTotalMessage);
    }

    // ------------------------------------------------------------------
    // Acompte
    // ------------------------------------------------------------------

    [Fact]
    public void AcompteNegatif_EstRefuse()
    {
        var act = () => SalePricingPolicy.Calculate(new[] { Line(1, 100m) }, 0m, depositAmount: -5m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.DepositNegativeMessage);
    }

    [Fact]
    public void AcompteEgalAuFinal_EstAccepte()
    {
        var final = (1 * 100m) - 10m;
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 100m) }, discountAmount: 10m, depositAmount: final);

        pricing.RemainingAmount.Should().Be(0m);
        pricing.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void AcompteSuperieurAuFinal_EstRefuse()
    {
        var final = (1 * 100m) - 10m;
        var act = () => SalePricingPolicy.Calculate(new[] { Line(1, 100m) }, discountAmount: 10m, depositAmount: final + 0.01m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.DepositAboveFinalMessage);
    }

    // ------------------------------------------------------------------
    // Chaîne complète des quatre montants
    // ------------------------------------------------------------------

    [Fact]
    public void LesQuatreMontants_SontCalculesExactement()
    {
        var lines = new[] { Line(2, 75.25m), Line(1, 49.50m) };
        var discount = 12.75m;
        var deposit = 60m;

        var pricing = SalePricingPolicy.Calculate(lines, discount, deposit);

        var expectedTotal = (2 * 75.25m) + (1 * 49.50m);
        pricing.TotalAmount.Should().Be(expectedTotal);
        pricing.DiscountAmount.Should().Be(discount);
        pricing.FinalAmount.Should().Be(expectedTotal - discount);
        pricing.DepositAmount.Should().Be(deposit);
        pricing.RemainingAmount.Should().Be(expectedTotal - discount - deposit);
    }

    // ------------------------------------------------------------------
    // PaymentStatus
    // ------------------------------------------------------------------

    [Fact]
    public void SansAcompte_EtSoldeDu_DonnePending()
    {
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 200m) }, 0m, depositAmount: 0m);

        pricing.RemainingAmount.Should().BeGreaterThan(0m);
        pricing.PaymentStatus.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void AcomptePartiel_EtSoldeDu_DonnePartial()
    {
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 200m) }, 0m, depositAmount: 50m);

        pricing.RemainingAmount.Should().BeGreaterThan(0m);
        pricing.PaymentStatus.Should().Be(PaymentStatus.Partial);
    }

    [Fact]
    public void SoldeNul_DonnePaid()
    {
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 200m) }, 0m, depositAmount: 1 * 200m);

        pricing.RemainingAmount.Should().Be(0m);
        pricing.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void VenteEntierementOfferte_DonnePaid()
    {
        // FinalAmount nul : il n'y a rien à devoir, donc rien n'est « en attente ».
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 0m) }, 0m, 0m);

        pricing.FinalAmount.Should().Be(0m);
        pricing.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public void LeStatutDePaiement_NEstJamaisLaisseAuDefautEf()
    {
        // Le défaut EF est Paid : une vente à crédit ne doit jamais en hériter (audit §22 C10).
        var pricing = SalePricingPolicy.Calculate(new[] { Line(1, 500m) }, 0m, depositAmount: 100m);

        pricing.PaymentStatus.Should().NotBe(PaymentStatus.Paid);
    }

    // ------------------------------------------------------------------
    // Dépassement de capacité decimal → erreur MÉTIER, jamais technique
    // ------------------------------------------------------------------

    [Fact]
    public void DepassementDecimal_EstConvertiEnErreurMetier()
    {
        // decimal.MaxValue × 2 dépasse la capacité : l'arithmétique decimal lève OverflowException, qui ne doit
        // jamais remonter brute à l'appelant.
        var act = () => SalePricingPolicy.Calculate(new[] { Line(2, decimal.MaxValue) }, 0m, 0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.AmountOutOfRangeMessage);
    }

    [Fact]
    public void DepassementDecimal_ParCumulDeLignes_EstConvertiEnErreurMetier()
    {
        var act = () => SalePricingPolicy.Calculate(
            new[] { Line(1, decimal.MaxValue), Line(1, decimal.MaxValue) },
            0m,
            0m);

        act.Should().Throw<BusinessRuleException>().WithMessage(SalePricingPolicy.AmountOutOfRangeMessage);
    }

    [Fact]
    public void DepassementDecimal_NeRemontePasDOverflowException()
    {
        var act = () => SalePricingPolicy.Calculate(new[] { Line(2, decimal.MaxValue) }, 0m, 0m);

        act.Should().NotThrow<OverflowException>();
    }
}
