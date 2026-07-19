using FluentAssertions;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-6B — <see cref="WorkshopSheetFingerprint"/> : empreinte technique déterministe servant à détecter qu'une
/// fiche atelier est devenue obsolète.
///
/// Prouve le déterminisme, la sensibilité à toute donnée technique réellement pertinente, l'insensibilité à
/// l'ordre de restitution, et l'<b>injectivité</b> de l'encodage (pas de collision par séparateur ni par
/// confusion null / chaîne vide).
/// </summary>
public sealed class WorkshopSheetFingerprintTests
{
    private static Order BuildOrder(params OrderItem[] items)
    {
        var order = new Order
        {
            OrderId = 1,
            OrderNumber = "CMD-000001",
            Notes = "Monter avec soin",
            Status = OrderStatus.InProgress
        };

        foreach (var item in items)
        {
            order.OrderItems.Add(item);
        }

        return order;
    }

    private static OrderItem BuildItem(long id = 1, OrderItemType type = OrderItemType.LensOd) => new()
    {
        OrderItemId = id,
        ItemType = type,
        ProductId = 10,
        Quantity = 1,
        UsageType = LensUsageType.Distance,
        Sphere = -1.25,
        Cylinder = -0.50,
        Axis = 90,
        Addition = 2.00,
        PrismValue = 1.00,
        PrismBase = PrismBase.In,
        VisualAcuity = "10/10"
    };

    // -------------------------------------------------------------------------------------------------------
    // Déterminisme
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Compute_MemeContenu_DonneToujoursLaMemeEmpreinte()
    {
        var a = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));
        var b = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        a.Should().Be(b);
    }

    [Fact]
    public void Compute_ProduitUnSha256Hexadecimal()
    {
        var fingerprint = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        fingerprint.Should().HaveLength(64);
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Compute_CommandeSansLigne_EstStable()
    {
        var a = WorkshopSheetFingerprint.Compute(BuildOrder());
        var b = WorkshopSheetFingerprint.Compute(BuildOrder());

        a.Should().Be(b);
    }

    // -------------------------------------------------------------------------------------------------------
    // Sensibilité aux données techniques
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Compute_ChangementDeSphere_ChangeLEmpreinte()
    {
        var before = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        var modified = BuildItem();
        modified.Sphere = -1.50;

        WorkshopSheetFingerprint.Compute(BuildOrder(modified)).Should().NotBe(before);
    }

    [Fact]
    public void Compute_ChangementInfimeDeSphere_ChangeLEmpreinte()
    {
        // Un format arrondi (F2) écraserait cette différence : le format aller-retour "R" la conserve.
        var before = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        var modified = BuildItem();
        modified.Sphere = -1.2500000001;

        WorkshopSheetFingerprint.Compute(BuildOrder(modified)).Should().NotBe(before);
    }

    [Fact]
    public void Compute_ChangementDeQuantite_ChangeLEmpreinte()
    {
        var before = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        var modified = BuildItem();
        modified.Quantity = 2;

        WorkshopSheetFingerprint.Compute(BuildOrder(modified)).Should().NotBe(before);
    }

    [Fact]
    public void Compute_ChangementDInstructions_ChangeLEmpreinte()
    {
        var before = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        var order = BuildOrder(BuildItem());
        order.Notes = "Monter avec soin — URGENT";

        WorkshopSheetFingerprint.Compute(order).Should().NotBe(before);
    }

    [Theory]
    [InlineData(nameof(OrderItem.Cylinder))]
    [InlineData(nameof(OrderItem.Axis))]
    [InlineData(nameof(OrderItem.Addition))]
    [InlineData(nameof(OrderItem.PrismValue))]
    [InlineData(nameof(OrderItem.PrismBase))]
    [InlineData(nameof(OrderItem.VisualAcuity))]
    [InlineData(nameof(OrderItem.UsageType))]
    [InlineData(nameof(OrderItem.ProductId))]
    [InlineData(nameof(OrderItem.ItemType))]
    public void Compute_ChaqueDonneeTechnique_InfluenceLEmpreinte(string propertyName)
    {
        var before = WorkshopSheetFingerprint.Compute(BuildOrder(BuildItem()));

        var modified = BuildItem();
        switch (propertyName)
        {
            case nameof(OrderItem.Cylinder): modified.Cylinder = -0.75; break;
            case nameof(OrderItem.Axis): modified.Axis = 100; break;
            case nameof(OrderItem.Addition): modified.Addition = 2.50; break;
            case nameof(OrderItem.PrismValue): modified.PrismValue = 2.00; break;
            case nameof(OrderItem.PrismBase): modified.PrismBase = PrismBase.Up; break;
            case nameof(OrderItem.VisualAcuity): modified.VisualAcuity = "8/10"; break;
            case nameof(OrderItem.UsageType): modified.UsageType = LensUsageType.Progressive; break;
            case nameof(OrderItem.ProductId): modified.ProductId = 11; break;
            case nameof(OrderItem.ItemType): modified.ItemType = OrderItemType.LensOg; break;
        }

        WorkshopSheetFingerprint.Compute(BuildOrder(modified)).Should().NotBe(before);
    }

    // -------------------------------------------------------------------------------------------------------
    // Périmètre exclu
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Compute_LeStatutDeCommande_NInfluencePasLEmpreinte()
    {
        // Sinon toute fiche deviendrait « obsolète » à la transition de statut suivante.
        var a = BuildOrder(BuildItem());
        a.Status = OrderStatus.InProgress;

        var b = BuildOrder(BuildItem());
        b.Status = OrderStatus.QualityCheck;

        WorkshopSheetFingerprint.Compute(a).Should().Be(WorkshopSheetFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_LEnTeteNonTechnique_NInfluencePasLEmpreinte()
    {
        var a = BuildOrder(BuildItem());
        var b = BuildOrder(BuildItem());
        b.OrderNumber = "CMD-999999";
        b.EstimatedDelivery = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        WorkshopSheetFingerprint.Compute(a).Should().Be(WorkshopSheetFingerprint.Compute(b));
    }

    // -------------------------------------------------------------------------------------------------------
    // Ordre déterministe
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Compute_OrdreDeRestitutionDifferent_MaisMemeOrdreMetier_DonneLaMemeEmpreinte()
    {
        var ascending = BuildOrder(BuildItem(1, OrderItemType.Frame), BuildItem(2, OrderItemType.LensOd));

        var descending = new Order { OrderId = 1, OrderNumber = "CMD-000001", Notes = "Monter avec soin" };
        descending.OrderItems.Add(BuildItem(2, OrderItemType.LensOd));
        descending.OrderItems.Add(BuildItem(1, OrderItemType.Frame));

        WorkshopSheetFingerprint.Compute(ascending).Should().Be(WorkshopSheetFingerprint.Compute(descending));
    }

    // -------------------------------------------------------------------------------------------------------
    // Injectivité de l'encodage
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Compute_ChainesContenantDesSeparateurs_NeCollisionnentPas()
    {
        // Un encodage par simple concaténation avec « | » ou « ; » collisionnerait ici.
        var a = BuildOrder(BuildItem());
        a.Notes = "A|B";

        var b = BuildOrder(BuildItem());
        b.Notes = "A";
        var bItem = b.OrderItems.First();
        bItem.VisualAcuity = "|B" + bItem.VisualAcuity;

        WorkshopSheetFingerprint.Compute(a).Should().NotBe(WorkshopSheetFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_DecoupageDifferentDesMemesCaracteres_NeCollisionnePas()
    {
        var a = BuildOrder(BuildItem());
        a.Notes = "AB";
        a.OrderItems.First().VisualAcuity = "C";

        var b = BuildOrder(BuildItem());
        b.Notes = "A";
        b.OrderItems.First().VisualAcuity = "BC";

        WorkshopSheetFingerprint.Compute(a).Should().NotBe(WorkshopSheetFingerprint.Compute(b));
    }

    [Fact]
    public void Compute_NullEtChaineVide_SontDistincts()
    {
        var withNull = BuildOrder(BuildItem());
        withNull.Notes = null;

        var withEmpty = BuildOrder(BuildItem());
        withEmpty.Notes = string.Empty;

        WorkshopSheetFingerprint.Compute(withNull).Should().NotBe(WorkshopSheetFingerprint.Compute(withEmpty));
    }

    [Fact]
    public void Compute_ValeurOptiqueNulle_EstDistincteDeZero()
    {
        var withNull = BuildItem();
        withNull.Cylinder = null;
        withNull.Axis = null;

        var withZero = BuildItem();
        withZero.Cylinder = 0d;
        withZero.Axis = null;

        WorkshopSheetFingerprint.Compute(BuildOrder(withNull))
            .Should().NotBe(WorkshopSheetFingerprint.Compute(BuildOrder(withZero)));
    }

    [Fact]
    public void Compute_CommandeNulle_LeveArgumentNullException()
    {
        var act = () => WorkshopSheetFingerprint.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
