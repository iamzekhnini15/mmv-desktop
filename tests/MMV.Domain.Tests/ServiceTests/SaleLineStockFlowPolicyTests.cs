using System;
using System.Linq;
using FluentAssertions;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-11 — <see cref="SaleLineStockFlowPolicy"/> : propriétaire unique de la classification du flux de
/// consommation du stock. La matrice est exhaustive sur les deux énumérations réelles du dépôt, et les valeurs
/// hors domaine sont explicitement couvertes.
/// </summary>
public sealed class SaleLineStockFlowPolicyTests
{
    /// <summary>Catégories dont le stock est consommé immédiatement à la vente.</summary>
    public static TheoryData<ProductCategoryEnum> CategoriesImmediates() => new()
    {
        ProductCategoryEnum.CLIPS,
        ProductCategoryEnum.PLASTIC,
        ProductCategoryEnum.MONTURE,
        ProductCategoryEnum.SOLAIRE,
    };

    /// <summary>Catégories dont le stock est consommé plus tard, à la fabrication.</summary>
    public static TheoryData<ProductCategoryEnum> CategoriesDifferees() => new()
    {
        ProductCategoryEnum.VERRE,
        ProductCategoryEnum.LENTILLE,
    };

    // ---------------------------------------------------------------------------------------------------------
    // Classification élémentaire — testée DIRECTEMENT, pas seulement à travers IsCompatible.
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderItemType.LensOd)]
    [InlineData(OrderItemType.LensOg)]
    public void IsFabricationItemType_EstVraiPourLesLignesVerre(OrderItemType itemType)
        => SaleLineStockFlowPolicy.IsFabricationItemType(itemType).Should().BeTrue();

    [Theory]
    [InlineData(OrderItemType.Frame)]
    [InlineData(OrderItemType.Accessory)]
    public void IsFabricationItemType_EstFauxPourLesLignesConsommeesALaVente(OrderItemType itemType)
        => SaleLineStockFlowPolicy.IsFabricationItemType(itemType).Should().BeFalse();

    [Theory]
    [MemberData(nameof(CategoriesDifferees))]
    public void IsDeferredStockCategory_EstVraiPourVerreEtLentille(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsDeferredStockCategory(category).Should().BeTrue();

    [Theory]
    [MemberData(nameof(CategoriesImmediates))]
    public void IsDeferredStockCategory_EstFauxPourLesCategoriesImmediates(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsDeferredStockCategory(category).Should().BeFalse();

    // ---------------------------------------------------------------------------------------------------------
    // Couples COMPATIBLES.
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(OrderItemType.LensOd, ProductCategoryEnum.VERRE)]
    [InlineData(OrderItemType.LensOd, ProductCategoryEnum.LENTILLE)]
    [InlineData(OrderItemType.LensOg, ProductCategoryEnum.VERRE)]
    [InlineData(OrderItemType.LensOg, ProductCategoryEnum.LENTILLE)]
    public void LigneVerre_SurCategorieDifferee_EstCompatible(OrderItemType itemType, ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(itemType, category).Should().BeTrue();

    [Theory]
    [MemberData(nameof(CategoriesImmediates))]
    public void LigneMonture_SurCategorieImmediate_EstCompatible(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(OrderItemType.Frame, category).Should().BeTrue();

    [Theory]
    [MemberData(nameof(CategoriesImmediates))]
    public void LigneAccessoire_SurCategorieImmediate_EstCompatible(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(OrderItemType.Accessory, category).Should().BeTrue();

    // ---------------------------------------------------------------------------------------------------------
    // Couples INCOMPATIBLES — les deux sens du défaut découvert en recette P3-11.
    // ---------------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(CategoriesImmediates))]
    public void LigneVerreOd_SurCategorieImmediate_EstIncompatible(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(OrderItemType.LensOd, category).Should().BeFalse(
            "c'est le cas du DOUBLE décrément : la ligne entre en commande par son type, et le produit sort déjà à la vente par sa catégorie");

    [Theory]
    [MemberData(nameof(CategoriesImmediates))]
    public void LigneVerreOg_SurCategorieImmediate_EstIncompatible(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(OrderItemType.LensOg, category).Should().BeFalse();

    [Theory]
    [MemberData(nameof(CategoriesDifferees))]
    public void LigneMonture_SurCategorieDifferee_EstIncompatible(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(OrderItemType.Frame, category).Should().BeFalse(
            "c'est le cas de l'ABSENCE de décrément : la catégorie exclut le produit de la vente, et son type l'exclut de la commande");

    [Theory]
    [MemberData(nameof(CategoriesDifferees))]
    public void LigneAccessoire_SurCategorieDifferee_EstIncompatible(ProductCategoryEnum category)
        => SaleLineStockFlowPolicy.IsCompatible(OrderItemType.Accessory, category).Should().BeFalse();

    // ---------------------------------------------------------------------------------------------------------
    // Valeurs hors domaine — aucun repli silencieux.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void TypeDeLigneInconnu_NEstJamaisCompatible()
    {
        var inconnu = (OrderItemType)9999;

        foreach (var category in Enum.GetValues<ProductCategoryEnum>())
        {
            SaleLineStockFlowPolicy.IsCompatible(inconnu, category).Should().BeFalse(
                "une valeur hors domaine ne doit jamais retomber en silence sur la classe « consommé à la vente »");
        }
    }

    [Fact]
    public void CategorieInconnue_NEstJamaisCompatible()
    {
        var inconnue = (ProductCategoryEnum)9999;

        foreach (var itemType in Enum.GetValues<OrderItemType>())
        {
            SaleLineStockFlowPolicy.IsCompatible(itemType, inconnue).Should().BeFalse();
        }
    }

    [Fact]
    public void DeuxValeursInconnues_NeSAccordentPas()
        => SaleLineStockFlowPolicy.IsCompatible((OrderItemType)9999, (ProductCategoryEnum)9999).Should().BeFalse();

    // ---------------------------------------------------------------------------------------------------------
    // Invariant structurel : la matrice couvre RÉELLEMENT tous les couples du dépôt, et chaque couple accepté
    // désigne exactement un moment de consommation.
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void ToutCoupleDefini_EstCompatibleSiEtSeulementSiLesDeuxClassesCoincident()
    {
        var couples = from itemType in Enum.GetValues<OrderItemType>()
                      from category in Enum.GetValues<ProductCategoryEnum>()
                      select (itemType, category);

        foreach (var (itemType, category) in couples)
        {
            var attendu = SaleLineStockFlowPolicy.IsFabricationItemType(itemType)
                          == SaleLineStockFlowPolicy.IsDeferredStockCategory(category);

            SaleLineStockFlowPolicy.IsCompatible(itemType, category).Should().Be(attendu);
        }
    }

    [Fact]
    public void LesDeuxEnumerations_SontCellesReellementPresentesDansLeDepot()
    {
        // Si une valeur est ajoutée à l'une des deux énumérations, ce test échoue et impose de trancher
        // explicitement sa classe de consommation, plutôt que de la laisser hériter d'un défaut silencieux.
        Enum.GetValues<OrderItemType>().Should().BeEquivalentTo(new[]
        {
            OrderItemType.Frame, OrderItemType.LensOd, OrderItemType.LensOg, OrderItemType.Accessory
        });

        Enum.GetValues<ProductCategoryEnum>().Should().BeEquivalentTo(new[]
        {
            ProductCategoryEnum.CLIPS, ProductCategoryEnum.PLASTIC, ProductCategoryEnum.MONTURE,
            ProductCategoryEnum.SOLAIRE, ProductCategoryEnum.LENTILLE, ProductCategoryEnum.VERRE
        });
    }

    [Fact]
    public void LeMessageMetier_EstStableEtSansDetailTechnique()
    {
        SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage
            .Should().Be("Le type de ligne ne correspond pas à la catégorie du produit.");

        SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage
            .Should().NotContainAny("OrderItemType", "ProductCategory", "SQLite", "Exception", "LensOd", "VERRE");
    }
}
