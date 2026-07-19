using FluentAssertions;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// P3-6B — <see cref="WorkshopSheetFactory"/> : construction pure d'une version de fiche atelier.
///
/// Prouve que le snapshot est <b>complet</b> (y compris les champs perdus par le DTO de lecture actuel),
/// <b>minimal</b> en données personnelles, robuste aux données incomplètes, et qu'il ne modifie jamais sa source.
/// </summary>
public sealed class WorkshopSheetFactoryTests
{
    private static readonly DateTime CreatedAt = new(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);

    private static Order BuildOrder(bool withCustomer = true, bool withProduct = true)
    {
        var order = new Order
        {
            OrderId = 7,
            OrderNumber = "CMD-000042",
            OrderDate = new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc),
            EstimatedDelivery = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            Notes = "Montage percé",
            Status = OrderStatus.InProgress
        };

        if (withCustomer)
        {
            order.Sale = new Sale
            {
                Customer = new Customer { FirstName = "Amina", LastName = "Benali", Phone = "0600000000", Email = "a@b.c" }
            };
        }

        order.OrderItems.Add(new OrderItem
        {
            OrderItemId = 1,
            ItemType = OrderItemType.Frame,
            ProductId = withProduct ? 100 : null,
            Product = withProduct
                ? new Product { ProductId = 100, Reference = "MON-1", Name = "Monture Alpha", Category = ProductCategoryEnum.MONTURE }
                : null,
            Quantity = 1
        });

        order.OrderItems.Add(new OrderItem
        {
            OrderItemId = 2,
            ItemType = OrderItemType.LensOd,
            ProductId = withProduct ? 200 : null,
            Product = withProduct
                ? new Product { ProductId = 200, Reference = "VER-1", Name = "Verre Ultra", Category = ProductCategoryEnum.VERRE }
                : null,
            Quantity = 1,
            UsageType = LensUsageType.Progressive,
            Sphere = 2.00,
            Cylinder = -1.00,
            Axis = 180,
            Addition = 2.50,
            PrismValue = 1.50,
            PrismBase = PrismBase.Out,
            VisualAcuity = "10/10"
        });

        return order;
    }

    // -------------------------------------------------------------------------------------------------------
    // En-tête et version
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Create_PremiereVersion_EstCouranteEtEnAttenteDeControle()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(), version: 1, CreatedAt);

        sheet.Version.Should().Be(1);
        sheet.IsCurrent.Should().BeTrue();
        sheet.QcStatus.Should().Be(WorkshopSheetQcStatus.Pending);
        sheet.QcComment.Should().BeNull();
        sheet.QcCompletedAt.Should().BeNull();
        sheet.CreatedAt.Should().Be(CreatedAt);
    }

    [Fact]
    public void Create_CopieLEnTeteDeCommande()
    {
        var order = BuildOrder();

        var sheet = WorkshopSheetFactory.Create(order, 1, CreatedAt);

        sheet.OrderId.Should().Be(order.OrderId);
        sheet.OrderNumberSnapshot.Should().Be("CMD-000042");
        sheet.OrderDateSnapshot.Should().Be(order.OrderDate);
        sheet.EstimatedDeliverySnapshot.Should().Be(order.EstimatedDelivery);
        sheet.InstructionsSnapshot.Should().Be("Montage percé");
    }

    [Fact]
    public void Create_RenseigneLEmpreinteTechnique()
    {
        var order = BuildOrder();

        var sheet = WorkshopSheetFactory.Create(order, 1, CreatedAt);

        sheet.TechnicalFingerprint.Should().Be(WorkshopSheetFingerprint.Compute(order));
    }

    [Fact]
    public void Create_VersionInvalide_LeveArgumentOutOfRange()
    {
        var act = () => WorkshopSheetFactory.Create(BuildOrder(), version: 0, CreatedAt);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_CommandeNulle_LeveArgumentNullException()
    {
        var act = () => WorkshopSheetFactory.Create(null!, 1, CreatedAt);

        act.Should().Throw<ArgumentNullException>();
    }

    // -------------------------------------------------------------------------------------------------------
    // Minimisation des données personnelles
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Create_NeCopieQueLeNomDuPorteur()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(), 1, CreatedAt);

        sheet.CustomerNameSnapshot.Should().Be("Amina Benali");
    }

    [Fact]
    public void WorkshopSheet_NePorteAucuneDonneePersonnelleAuDelaDuNom()
    {
        // Garantie STRUCTURELLE : téléphone, email, adresse et montants n'existent pas dans le modèle de fiche.
        // La minimisation est obtenue par construction, pas par un filtrage qu'on pourrait oublier.
        var propertyNames = typeof(WorkshopSheet).GetProperties().Select(p => p.Name).ToArray();

        propertyNames.Should().NotContain(n => n.Contains("Phone", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(n => n.Contains("Email", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(n => n.Contains("Address", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(n => n.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(n => n.Contains("Price", StringComparison.OrdinalIgnoreCase));
        propertyNames.Should().NotContain(n => n.Contains("Deposit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_SansClient_UtiliseUnLibelleNeutre()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(withCustomer: false), 1, CreatedAt);

        sheet.CustomerNameSnapshot.Should().Be(WorkshopSheetFactory.UnknownCustomerLabel);
    }

    [Fact]
    public void Create_ClientSansNom_UtiliseUnLibelleNeutre()
    {
        var order = BuildOrder();
        order.Sale!.Customer = new Customer { FirstName = " ", LastName = " " };

        var sheet = WorkshopSheetFactory.Create(order, 1, CreatedAt);

        sheet.CustomerNameSnapshot.Should().Be(WorkshopSheetFactory.UnknownCustomerLabel);
    }

    // -------------------------------------------------------------------------------------------------------
    // Lignes : snapshot complet
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Create_CopieToutesLesLignesReellesDansUnOrdreDeterministe()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(), 1, CreatedAt);

        sheet.Items.Should().HaveCount(2);
        sheet.Items.Select(i => i.Position).Should().BeEquivalentTo(new[] { 0, 1 }, o => o.WithStrictOrdering());
        sheet.Items.Select(i => i.ItemType).Should().ContainInOrder(OrderItemType.Frame, OrderItemType.LensOd);
    }

    [Fact]
    public void Create_CopieLesChampsPerdusParLeDtoDeLecture()
    {
        // OrderDetailsItemDto ne transporte PAS usage / prisme / base / acuité (constat de l'audit P3-6B) :
        // le snapshot doit les récupérer depuis OrderItem, faute de quoi le bon d'atelier serait incomplet.
        var sheet = WorkshopSheetFactory.Create(BuildOrder(), 1, CreatedAt);

        var lens = sheet.Items.Single(i => i.ItemType == OrderItemType.LensOd);
        lens.UsageType.Should().Be(LensUsageType.Progressive);
        lens.PrismValue.Should().Be(1.50);
        lens.PrismBase.Should().Be(PrismBase.Out);
        lens.VisualAcuity.Should().Be("10/10");
        lens.Addition.Should().Be(2.50);
    }

    [Fact]
    public void Create_CopieLesDonneesProduitParValeur()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(), 1, CreatedAt);

        var frame = sheet.Items.Single(i => i.ItemType == OrderItemType.Frame);
        frame.ProductReferenceSnapshot.Should().Be("MON-1");
        frame.ProductNameSnapshot.Should().Be("Monture Alpha");
        frame.ProductCategorySnapshot.Should().Be(nameof(ProductCategoryEnum.MONTURE));
        frame.SourceProductId.Should().Be(100);
    }

    [Fact]
    public void Create_LigneSansProduit_EstConserveeAvecDesValeursNeutres()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(withProduct: false), 1, CreatedAt);

        sheet.Items.Should().HaveCount(2);
        sheet.Items.Should().OnlyContain(i => i.ProductReferenceSnapshot == null
                                              && i.ProductNameSnapshot == null
                                              && i.ProductCategorySnapshot == null
                                              && i.SourceProductId == null);

        // Les données optiques restent intégralement copiées : c'est le cœur du bon d'atelier.
        sheet.Items.Single(i => i.ItemType == OrderItemType.LensOd).SourceSphere.Should().Be(2.00);
    }

    [Fact]
    public void Create_CommandeSansLigne_ProduitUneFicheSansLigne()
    {
        var order = BuildOrder();
        order.OrderItems.Clear();

        var sheet = WorkshopSheetFactory.Create(order, 1, CreatedAt);

        sheet.Items.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------------------------------------
    // Deux notations conservées
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Create_ConserveLaNotationSourceEtLaNotationTransposee()
    {
        var sheet = WorkshopSheetFactory.Create(BuildOrder(), 1, CreatedAt);

        var lens = sheet.Items.Single(i => i.ItemType == OrderItemType.LensOd);

        // Source intacte.
        lens.SourceSphere.Should().Be(2.00);
        lens.SourceCylinder.Should().Be(-1.00);
        lens.SourceAxis.Should().Be(180);

        // Notation atelier dérivée (+2.00 (−1.00) 180 ⇔ +1.00 (+1.00) 90).
        lens.HasTransposition.Should().BeTrue();
        lens.TransposedSphere.Should().Be(1.00);
        lens.TransposedCylinder.Should().Be(1.00);
        lens.TransposedAxis.Should().Be(90);
    }

    [Fact]
    public void Create_LigneSansCylindre_NaPasDeNotationTransposee()
    {
        var order = BuildOrder();
        var frame = order.OrderItems.Single(i => i.ItemType == OrderItemType.Frame);
        frame.Sphere = null;
        frame.Cylinder = null;
        frame.Axis = null;

        var sheet = WorkshopSheetFactory.Create(order, 1, CreatedAt);

        var snapshot = sheet.Items.Single(i => i.ItemType == OrderItemType.Frame);
        snapshot.HasTransposition.Should().BeFalse();
        snapshot.TransposedSphere.Should().BeNull();
        snapshot.TransposedCylinder.Should().BeNull();
        snapshot.TransposedAxis.Should().BeNull();
    }

    // -------------------------------------------------------------------------------------------------------
    // Source jamais modifiée
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Create_NeModifieJamaisLaCommandeNiSesLignesNiLeCatalogue()
    {
        var order = BuildOrder();
        var lens = order.OrderItems.Single(i => i.ItemType == OrderItemType.LensOd);
        var product = lens.Product!;

        WorkshopSheetFactory.Create(order, 1, CreatedAt);

        // Commande.
        order.OrderNumber.Should().Be("CMD-000042");
        order.Notes.Should().Be("Montage percé");
        order.Status.Should().Be(OrderStatus.InProgress);

        // Ligne : la transposition ne doit RIEN écrire ici.
        lens.Sphere.Should().Be(2.00);
        lens.Cylinder.Should().Be(-1.00);
        lens.Axis.Should().Be(180);
        lens.Addition.Should().Be(2.50);
        lens.PrismValue.Should().Be(1.50);
        lens.PrismBase.Should().Be(PrismBase.Out);
        lens.VisualAcuity.Should().Be("10/10");
        lens.UsageType.Should().Be(LensUsageType.Progressive);

        // Catalogue.
        product.Reference.Should().Be("VER-1");
        product.Name.Should().Be("Verre Ultra");
    }
}
