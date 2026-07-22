using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Application.Tests.Acceptance;

/// <summary>
/// Scénario S9 (P3-11) — <b>gate d'intégrité</b> sur la classification d'une ligne de vente.
/// </summary>
/// <remarks>
/// <para>
/// <b>Historique de ce gate.</b> Avant la remédiation P3-11, deux clés indépendantes gouvernaient le stock d'une
/// ligne de vente sans jamais être croisées : l'appartenance à la commande de fabrication était décidée par
/// l'<b>ItemType de la ligne</b>, tandis que le saut du décrément à la vente l'était par la <b>Category du
/// produit</b>. Ce gate a prouvé <b>par exécution</b> qu'une ligne dont les deux clés se contredisent était
/// <b>acceptée</b>, et qu'il en résultait un <b>double décrément</b> (cas A) ou <b>aucun décrément</b> (cas B).
/// Il est resté rouge jusqu'à ce que la règle manquante soit arbitrée puis implémentée.
/// </para>
/// <para>
/// <b>L'invariant n'a pas été affaibli, il a été tranché.</b> La formulation d'origine admettait deux issues
/// valides — refus métier, ou décrément exactement unique. La remédiation ayant fait du <b>refus</b> l'issue
/// retenue (la ligne est désormais opposée à la saisie, dans <c>RegisterSaleUseCase</c>, avant toute écriture),
/// ces scénarios exigent maintenant cette issue précise et vérifient qu'elle ne laisse <b>strictement aucune</b>
/// trace. Les deux cas sont conservés, aucun n'est ignoré, et aucun comportement défectueux n'est gravé comme
/// attendu.
/// </para>
/// <para>
/// La vente étant refusée dès son enregistrement, aucune transition de statut n'est plus tentée : il n'existe ni
/// commande, ni fabrication à faire avancer.
/// </para>
/// </remarks>
public sealed class SaleLineClassificationAcceptanceTests : AcceptanceScenarioBase
{
    private const int StockInitial = 6;
    private const decimal PrixLigne = 100.00m;

    /// <summary>
    /// Cas A — produit de catégorie <b>monture</b> vendu sur une ligne typée <c>LensOd</c>. C'était le
    /// <b>double</b> décrément : sorti à la vente par sa catégorie, puis à la fabrication par son type.
    /// </summary>
    [Fact]
    public async Task LigneVerreSurProduitMonture_EstRefuseeSansAucuneEcriture()
    {
        var databasePath = CreateMigratedDatabase();
        long productId;

        await using (var scope = OpenScope(databasePath))
        {
            var supplierId = await AcceptanceActs.CreerFournisseurAsync(scope);
            productId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, "MON-9001", "Monture vendue comme verre",
                ProductCategoryEnum.MONTURE, StockInitial, 1, PrixLigne);
            var customerId = await AcceptanceActs.CreerClientAsync(scope);

            var command = new RegisterSaleCommand
            {
                CustomerId = customerId,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new List<RegisterSaleLineCommand>
                {
                    new()
                    {
                        ProductId = productId,
                        ItemType = OrderItemType.LensOd,
                        Quantity = 1,
                        UnitPrice = PrixLigne,
                        Sphere = AcceptanceData.OdSphere,
                        Cylinder = AcceptanceData.OdCylinder,
                        Axis = AcceptanceData.OdAxis
                    }
                }
            };

            var act = () => scope.RegisterSale.ExecuteAsync(command);

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>(
                "une ligne verre sur un produit monture doit être opposée à la saisie");
            thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);
        }

        await AssertAucuneEcritureAsync(databasePath, productId);
    }

    /// <summary>
    /// Cas B — produit de catégorie <b>verre</b> vendu sur une ligne typée <c>Frame</c>. C'était l'<b>absence
    /// totale</b> de décrément : exclu de la vente par sa catégorie, absent de la commande faute de type verre —
    /// une vente encaissée sans aucune contrepartie d'inventaire.
    /// </summary>
    [Fact]
    public async Task LigneMontureSurProduitVerre_EstRefuseeSansAucuneEcriture()
    {
        var databasePath = CreateMigratedDatabase();
        long productId;

        await using (var scope = OpenScope(databasePath))
        {
            var supplierId = await AcceptanceActs.CreerFournisseurAsync(scope);
            productId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, "VER-9002", "Verre vendu comme monture",
                ProductCategoryEnum.VERRE, StockInitial, 1, PrixLigne);
            var customerId = await AcceptanceActs.CreerClientAsync(scope);

            var command = new RegisterSaleCommand
            {
                CustomerId = customerId,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new List<RegisterSaleLineCommand>
                {
                    new()
                    {
                        ProductId = productId,
                        ItemType = OrderItemType.Frame,
                        Quantity = 1,
                        UnitPrice = PrixLigne
                    }
                }
            };

            var act = () => scope.RegisterSale.ExecuteAsync(command);

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>(
                "une ligne monture sur un produit verre doit être opposée à la saisie");
            thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);
        }

        await AssertAucuneEcritureAsync(databasePath, productId);
    }

    /// <summary>
    /// Vérifie, depuis un contexte <b>neuf</b>, qu'un refus n'a laissé aucune trace : aucun document, aucun
    /// mouvement, aucun stock touché, aucune notification, et <b>aucun numéro consommé</b>.
    /// </summary>
    private static async Task AssertAucuneEcritureAsync(string databasePath, long productId)
    {
        await using var read = OpenFreshRead(databasePath);

        (await read.Sales.CountAsync()).Should().Be(0, "aucune vente ne doit subsister");
        (await read.SaleItems.CountAsync()).Should().Be(0, "aucune ligne de vente ne doit subsister");
        (await read.Orders.CountAsync()).Should().Be(0, "aucune commande de fabrication ne doit être créée");
        (await read.OrderItems.CountAsync()).Should().Be(0, "aucun article de commande ne doit être créé");
        (await read.StockMovements.CountAsync()).Should().Be(0, "aucun mouvement de stock ne doit être créé");
        (await read.Notifications.CountAsync()).Should().Be(0, "aucune notification ne doit être créée");

        var product = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == productId);
        product.StockQuantity.Should().Be(StockInitial, "un refus ne touche jamais le stock");

        // Les séquences sont pré-déclarées par le modèle à 0 : le critère est qu'aucune n'ait été CONSOMMÉE.
        var sequences = await read.DocumentSequences.AsNoTracking().Select(s => s.CurrentValue).ToListAsync();
        sequences.Should().OnlyContain(v => v == 0,
            "le refus précède la numérotation : ni numéro de vente ni numéro de commande ne doit être attribué");
    }
}
