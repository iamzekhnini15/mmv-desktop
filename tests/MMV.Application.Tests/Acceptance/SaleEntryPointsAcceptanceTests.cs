using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.SetCustomerArchived;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using Xunit;

namespace MMV.Application.Tests.Acceptance;

/// <summary>
/// Scénarios S2 et S3 (P3-11) — les deux points d'entrée du parcours qui ne mènent <b>pas</b> à une fabrication :
/// la vente au comptoir anonyme, et le refus d'une vente pour un client archivé.
/// </summary>
public sealed class SaleEntryPointsAcceptanceTests : AcceptanceScenarioBase
{
    /// <summary>
    /// S2 — vente <b>sans client</b>, monture seule, soldée immédiatement.
    /// </summary>
    /// <remarks>
    /// Prouve la branche « aucun article à consommation différée ⇒ aucune commande », que S1 ne peut pas
    /// atteindre, ainsi que le motif de mouvement propre à l'absence de client. Confirme aussi, sans le
    /// contourner, que le solde d'une vente sans commande serait <b>irréglable</b>
    /// (<c>SettleOrderBalanceCommand</c> porte un <c>OrderId</c>, jamais un <c>SaleId</c>) : le scénario solde
    /// donc la vente <b>à l'encaissement</b>, ce qui est le geste réel du comptoir.
    /// </remarks>
    [Fact]
    public async Task VenteSansClient_ProduitUneVenteAnonymeCoherente_EtAucuneCommandeSansVerre()
    {
        var databasePath = CreateMigratedDatabase();
        long frameId, saleId;
        string saleNumber;

        await using (var scope = OpenScope(databasePath))
        {
            var supplierId = await AcceptanceActs.CreerFournisseurAsync(scope);
            frameId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.FrameReference, "Monture comptoir",
                ProductCategoryEnum.MONTURE, AcceptanceData.FrameInitialStock,
                AcceptanceData.FrameAlertThreshold, AcceptanceData.FramePrice);

            // Acompte égal au montant final : la vente est soldée sur place.
            var result = await scope.RegisterSale.ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = null,
                IsCounterSale = true,
                DepositAmount = AcceptanceData.FramePrice,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new List<RegisterSaleLineCommand>
                {
                    new()
                    {
                        ProductId = frameId,
                        ItemType = OrderItemType.Frame,
                        Quantity = 1,
                        UnitPrice = AcceptanceData.FramePrice
                    }
                }
            });

            saleId = result.SaleId;
            saleNumber = result.SaleNumber;

            result.OrderId.Should().BeNull("sans article à consommation différée, aucune commande ne naît");
            result.RemainingAmount.Should().Be(0m);
        }

        await using var read = OpenFreshRead(databasePath);

        var sale = await read.Sales.AsNoTracking().SingleAsync(s => s.SaleId == saleId);
        sale.CustomerId.Should().BeNull("une vente au comptoir sans client est légitime depuis P3-7");
        sale.FinalAmount.Should().Be(AcceptanceData.FramePrice);
        sale.DepositAmount.Should().Be(AcceptanceData.FramePrice);
        sale.RemainingAmount.Should().Be(0m);
        sale.PaymentStatus.Should().Be(PaymentStatus.Paid);
        sale.Status.Should().Be(SaleStatus.Delivered, "sans verre à attendre, la vente naît livrée");

        (await read.Orders.CountAsync()).Should().Be(0, "aucune commande de fabrication");
        (await read.OrderItems.CountAsync()).Should().Be(0);
        (await read.WorkshopSheets.CountAsync()).Should().Be(0);

        // La monture sort du stock à la vente, exactement une fois.
        var frame = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == frameId);
        frame.StockQuantity.Should().Be(AcceptanceData.FrameInitialStock - 1);

        var movements = await read.StockMovements.AsNoTracking().ToListAsync();
        movements.Should().HaveCount(1);
        var movement = movements.Single();
        movement.MovementType.Should().Be(StockMovementType.Out);
        movement.Quantity.Should().Be(-1, "convention de signe P3-5 : une sortie porte un delta négatif");
        movement.Reason.Should().Contain(saleNumber);
        movement.Reason.Should().Contain("Vente sans client",
            "le motif reste lisible plutôt que d'afficher un « Client # » sans identifiant");

        (await read.Notifications.CountAsync()).Should().Be(0,
            "aucune transition de commande et aucun règlement différé : aucune notification");
    }

    /// <summary>
    /// S3 — vente pour un client <b>archivé</b> : refusée, et sans la moindre trace dans aucune table du parcours.
    /// </summary>
    /// <remarks>
    /// Le refus lui-même est déjà couvert par un test ciblé. Ce que seule la recette peut prouver, c'est
    /// l'<b>absence d'effet transversal</b> : ni vente, ni ligne, ni commande, ni article de commande, ni
    /// mouvement, ni stock touché, ni <b>numéro consommé</b>.
    /// </remarks>
    [Fact]
    public async Task VentePourClientArchive_EstRefusee_EtNeLaisseAucuneTraceDansLeParcours()
    {
        var databasePath = CreateMigratedDatabase();
        long frameId, lensOdId, lensOgId, customerId;

        await using (var scope = OpenScope(databasePath))
        {
            var supplierId = await AcceptanceActs.CreerFournisseurAsync(scope);
            frameId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.FrameReference, "Monture Alpha",
                ProductCategoryEnum.MONTURE, AcceptanceData.FrameInitialStock,
                AcceptanceData.FrameAlertThreshold, AcceptanceData.FramePrice);
            lensOdId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.LensOdReference, "Verre OD",
                ProductCategoryEnum.VERRE, AcceptanceData.LensInitialStock,
                AcceptanceData.LensAlertThreshold, AcceptanceData.LensPrice);
            lensOgId = await AcceptanceActs.CreerProduitAsync(
                scope, supplierId, AcceptanceData.LensOgReference, "Verre OG",
                ProductCategoryEnum.VERRE, AcceptanceData.LensInitialStock,
                AcceptanceData.LensAlertThreshold, AcceptanceData.LensPrice);

            customerId = await AcceptanceActs.CreerClientAsync(scope);
        }

        // --- Acte : archiver le client (use case public, jamais une mutation directe).
        await using (var scope = OpenScope(databasePath))
        {
            var archived = await scope.SetCustomerArchived.ExecuteAsync(new SetCustomerArchivedCommand
            {
                CustomerId = customerId,
                IsArchived = true
            });
            archived.CustomerFound.Should().BeTrue();
        }

        // --- Acte : tenter la vente. Le refus est acquis DANS la transaction, de façon atomique.
        await using (var scope = OpenScope(databasePath))
        {
            var act = () => scope.RegisterSale.ExecuteAsync(
                AcceptanceActs.VenteLunettesCompletes(customerId, frameId, lensOdId, lensOgId));

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
            thrown.Which.Message.Should().Be(RegisterSaleUseCase.CustomerArchivedMessage);
        }

        // --- Assert : aucune trace, lue depuis un contexte neuf.
        await using var read = OpenFreshRead(databasePath);

        (await read.Sales.CountAsync()).Should().Be(0);
        (await read.SaleItems.CountAsync()).Should().Be(0);
        (await read.Orders.CountAsync()).Should().Be(0);
        (await read.OrderItems.CountAsync()).Should().Be(0);
        (await read.StockMovements.CountAsync()).Should().Be(0);
        (await read.Notifications.CountAsync()).Should().Be(0);
        (await read.WorkshopSheets.CountAsync()).Should().Be(0);

        var products = await read.Products.AsNoTracking().OrderBy(p => p.ProductId).ToListAsync();
        products.Single(p => p.ProductId == frameId).StockQuantity.Should().Be(AcceptanceData.FrameInitialStock);
        products.Single(p => p.ProductId == lensOdId).StockQuantity.Should().Be(AcceptanceData.LensInitialStock);
        products.Single(p => p.ProductId == lensOgId).StockQuantity.Should().Be(AcceptanceData.LensInitialStock);

        var sequences = await read.DocumentSequences.AsNoTracking().Select(s => s.CurrentValue).ToListAsync();
        sequences.Should().OnlyContain(v => v == 0,
            "le refus précède la numérotation : aucun numéro de vente ni de commande n'est consommé");

        // Le client reste archivé — le refus n'a rien changé à son état.
        var customer = await read.Customers.AsNoTracking().SingleAsync(c => c.CustomerId == customerId);
        customer.IsArchived.Should().BeTrue();
    }
}
