using FluentAssertions;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-7 — <see cref="SaleLineOpticsPolicy"/> : invariants optiques minimaux d'une ligne de vente, strictement
/// ceux déjà établis par P3-3. Les champs optiques restent <b>facultatifs</b> : aucune ordonnance complète n'est
/// exigée.
/// </summary>
public sealed class SaleLineOpticsPolicyTests
{
    private static SaleLineOptics Optics(
        double? sphere = null,
        double? cylinder = null,
        int? axis = null,
        double? addition = null,
        double? prismValue = null,
        PrismBase? prismBase = null)
        => new(sphere, cylinder, axis, addition, prismValue, prismBase);

    private static SaleLineOptics Validate(SaleLineOptics optics, OrderItemType itemType = OrderItemType.LensOd)
        => SaleLineOpticsPolicy.ValidateAndNormalize(itemType, optics);

    // ------------------------------------------------------------------
    // Données partielles : facultatives, acceptées
    // ------------------------------------------------------------------

    [Fact]
    public void DonneesOptiquesVides_SontAcceptees()
    {
        var act = () => Validate(Optics());

        act.Should().NotThrow("aucun champ optique n'est rendu obligatoire par P3-7");
    }

    [Fact]
    public void DonneesOptiquesPartielles_SontAcceptees()
    {
        var result = Validate(Optics(sphere: -2.5, addition: 2.0));

        result.Sphere.Should().Be(-2.5);
        result.Addition.Should().Be(2.0);
        result.Axis.Should().BeNull("aucun axe n'est inventé");
    }

    // ------------------------------------------------------------------
    // Normalisation de l'axe
    // ------------------------------------------------------------------

    [Fact]
    public void AxeZero_EstNormaliseA180()
    {
        var result = Validate(Optics(cylinder: -1.25, axis: 0));

        result.Axis.Should().Be(180, "0° et 180° désignent la même orientation (convention produit P3-3B)");
    }

    [Fact]
    public void AxeNonNul_EstConserveInchange()
    {
        var result = Validate(Optics(cylinder: -1.25, axis: 90));

        result.Axis.Should().Be(90);
    }

    [Fact]
    public void AxeAbsent_ResteAbsent()
    {
        var result = Validate(Optics(sphere: -1.0));

        result.Axis.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // Cylindre ⇄ axe
    // ------------------------------------------------------------------

    [Fact]
    public void CylindreNonNul_SansAxe_EstRefuse()
    {
        var act = () => Validate(Optics(cylinder: -1.5));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.AxisRequiredMessage);
    }

    [Fact]
    public void AxeSansCylindreOrientable_EstRefuse()
    {
        var act = () => Validate(Optics(axis: 90));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.AxisWithoutCylinderMessage);
    }

    [Fact]
    public void AxeAvecCylindreNul_EstRefuse()
    {
        // Cylindre 0 = « pas d'astigmatisme » : il n'y a rien à orienter.
        var act = () => Validate(Optics(cylinder: 0d, axis: 90));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.AxisWithoutCylinderMessage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(181)]
    public void AxeHorsPlage_EstRefuse(int axis)
    {
        var act = () => Validate(Optics(cylinder: -1.0, axis: axis));

        act.Should().Throw<BusinessRuleException>().WithMessage(SaleLineOpticsPolicy.AxisOutOfRangeMessage);
    }

    // ------------------------------------------------------------------
    // Prisme ⇄ base
    // ------------------------------------------------------------------

    [Fact]
    public void PrismeSansBase_EstRefuse()
    {
        var act = () => Validate(Optics(prismValue: 2.0));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.PrismBaseRequiredMessage);
    }

    [Fact]
    public void BaseSansPrisme_EstRefusee()
    {
        var act = () => Validate(Optics(prismBase: PrismBase.Up));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.PrismBaseWithoutValueMessage);
    }

    [Fact]
    public void PrismeNegatif_EstRefuse()
    {
        var act = () => Validate(Optics(prismValue: -1.0, prismBase: PrismBase.Up));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.PrismValueNegativeMessage);
    }

    [Fact]
    public void PrismeAvecBase_EstAccepte()
    {
        var result = Validate(Optics(prismValue: 2.0, prismBase: PrismBase.Down));

        result.PrismValue.Should().Be(2.0);
        result.PrismBase.Should().Be(PrismBase.Down);
    }

    // ------------------------------------------------------------------
    // Valeurs finies
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void SphereNonFinie_EstRefusee(double value)
    {
        var act = () => Validate(Optics(sphere: value));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.FiniteValueMessage);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CylindreNonFini_EstRefuse(double value)
    {
        var act = () => Validate(Optics(cylinder: value));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.FiniteValueMessage);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void AdditionNonFinie_EstRefusee(double value)
    {
        var act = () => Validate(Optics(addition: value));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.FiniteValueMessage);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PrismeNonFini_EstRefuse(double value)
    {
        var act = () => Validate(Optics(prismValue: value, prismBase: PrismBase.Up));

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.FiniteValueMessage);
    }

    // ------------------------------------------------------------------
    // Périmètre : lignes non-verre
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(OrderItemType.Frame)]
    [InlineData(OrderItemType.Accessory)]
    public void LigneNonVerre_NEstPasSoumiseAuxReglesOptiques(OrderItemType itemType)
    {
        // Comportement d'origine conservé : les règles optiques ne s'appliquent qu'aux verres OD/OG.
        var optics = Optics(axis: 90);

        var result = SaleLineOpticsPolicy.ValidateAndNormalize(itemType, optics);

        result.Should().Be(optics, "une ligne non-verre est renvoyée inchangée");
    }

    [Fact]
    public void LigneVerreOg_EstSoumiseAuxMemesRegles()
    {
        var act = () => Validate(Optics(cylinder: -1.5), OrderItemType.LensOg);

        act.Should().Throw<BusinessRuleException>().WithMessage(PrescriptionValidator.AxisRequiredMessage);
    }

    // ------------------------------------------------------------------
    // Aucune mutation d'ordonnance
    // ------------------------------------------------------------------

    [Fact]
    public void LaPolitique_EstPureEtNeMuteRienEnEntree()
    {
        // Les données optiques d'une vente sont un INSTANTANÉ COPIÉ : aucune ordonnance n'est lue ni modifiée
        // (aucune FK PrescriptionId n'existe). La structure d'entrée est un record struct immuable : la valeur
        // canonique est RENVOYÉE, jamais écrite dans l'entrée.
        var input = Optics(cylinder: -1.0, axis: 0);

        var result = Validate(input);

        input.Axis.Should().Be(0, "l'entrée n'est jamais mutée");
        result.Axis.Should().Be(180, "la valeur canonique est produite dans la sortie");
    }
}
