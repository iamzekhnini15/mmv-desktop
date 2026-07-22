using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Products.SetProductActive;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Constants;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using Xunit;

namespace MMV.Application.Tests.Acceptance;

/// <summary>
/// Scénarios S7 et S8 (P3-11) — fin de parcours : rejeu du règlement, et désactivation d'un produit déjà engagé
/// dans un parcours en cours.
/// </summary>
public sealed class SettlementAndCatalogAcceptanceTests : AcceptanceScenarioBase
{
    private sealed record Parcours(
        string DatabasePath,
        long FrameId,
        long LensOdId,
        long LensOgId,
        long CustomerId,
        long SaleId,
        long OrderId,
        string OrderNumber);

    /// <summary>Monte le parcours nominal jusqu'à <c>InProgress</c> (fiche générée, verres décrémentés).</summary>
    private async Task<Parcours> MonterParcoursJusquEnFabricationAsync(string fileName)
    {
        var databasePath = CreateMigratedDatabase(fileName);
        long frameId, lensOdId, lensOgId, customerId, saleId, orderId;
        string orderNumber;

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

        await using (var scope = OpenScope(databasePath))
        {
            var sale = await scope.RegisterSale.ExecuteAsync(
                AcceptanceActs.VenteLunettesCompletes(customerId, frameId, lensOdId, lensOgId));
            saleId = sale.SaleId;
            orderId = sale.OrderId!.Value;
            orderNumber = sale.OrderNumber!;
        }

        await AcceptanceActs.AvancerStatutAsync(databasePath, orderId, OrderStatus.New, OrderStatus.ToFabricate);
        await AcceptanceActs.AvancerStatutAsync(databasePath, orderId, OrderStatus.ToFabricate, OrderStatus.InProgress);

        return new Parcours(databasePath, frameId, lensOdId, lensOgId, customerId, saleId, orderId, orderNumber);
    }

    /// <summary>Poursuit un parcours de <c>InProgress</c> jusqu'à <c>Delivered</c>, contrôle qualité compris.</summary>
    private static async Task MenerJusquALaLivraisonAsync(Parcours parcours)
    {
        await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        await using (var scope = OpenScopeFor(parcours.DatabasePath))
        {
            var current = await scope.GetCurrentWorkshopSheet.ExecuteAsync(
                new GetCurrentWorkshopSheetQuery { OrderId = parcours.OrderId });
            current.SheetFound.Should().BeTrue();

            var qc = await scope.ValidateWorkshopSheetQc.ExecuteAsync(new ValidateWorkshopSheetQcCommand
            {
                WorkshopSheetId = current.Sheet!.WorkshopSheetId,
                Comment = "Montage conforme"
            });
            qc.Sheet!.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        }

        await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.QualityCheck, OrderStatus.Ready);
        await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.Ready, OrderStatus.Delivered);
    }

    // =========================================================================================================
    // S7 — rejeu du règlement du solde.
    // =========================================================================================================

    /// <summary>
    /// S7 — le solde n'est encaissé qu'<b>une seule fois</b>, et n'est notifié qu'une seule fois, même rejoué.
    /// </summary>
    /// <remarks>
    /// La plus-value sur le test ciblé existant est que les montants proviennent d'une vente <b>réellement issue
    /// du parcours</b>, calculés par <c>SalePricingPolicy</c>, et non d'une vente fabriquée à la main.
    /// </remarks>
    [Fact]
    public async Task ReglementDuSoldeRejoue_NEncaisseQuUneFois_EtNeNotifieQuUneFois()
    {
        var parcours = await MonterParcoursJusquEnFabricationAsync("s7.db");
        await MenerJusquALaLivraisonAsync(parcours);

        // --- Acte : premier règlement.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var settle = await scope.SettleOrderBalance.ExecuteAsync(
                new SettleOrderBalanceCommand { OrderId = parcours.OrderId });

            settle.OrderFound.Should().BeTrue();
            settle.AlreadySettled.Should().BeFalse();
            settle.AmountEncashed.Should().Be(AcceptanceData.ExpectedRemaining);
        }

        // --- Acte : REJEU depuis une portée neuve (second poste, second clic).
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var replay = await scope.SettleOrderBalance.ExecuteAsync(
                new SettleOrderBalanceCommand { OrderId = parcours.OrderId });

            replay.OrderFound.Should().BeTrue();
            replay.AlreadySettled.Should().BeTrue("le solde est déjà encaissé");
            replay.AmountEncashed.Should().Be(0m, "aucun second encaissement");
        }

        // --- Assert : état monétaire final, lu depuis un contexte neuf.
        await using var read = OpenFreshRead(parcours.DatabasePath);

        var sale = await read.Sales.AsNoTracking().SingleAsync(s => s.SaleId == parcours.SaleId);
        sale.FinalAmount.Should().Be(AcceptanceData.ExpectedFinal);
        sale.DepositAmount.Should().Be(AcceptanceData.ExpectedFinal, "l'acompte est porté au montant final, une fois");
        sale.RemainingAmount.Should().Be(0m);
        sale.PaymentStatus.Should().Be(PaymentStatus.Paid);

        var paymentNotifications = await read.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationTypes.PaymentReceived)
            .ToListAsync();
        paymentNotifications.Should().HaveCount(1, "un seul encaissement ⇒ une seule notification de paiement");
    }

    // =========================================================================================================
    // S8 — désactivation d'un produit déjà engagé.
    // =========================================================================================================

    /// <summary>
    /// S8 — désactiver un produit déjà vendu conserve intégralement l'historique et ne bloque pas le parcours
    /// engagé, tout en interdisant toute <b>nouvelle</b> vente de ce produit.
    /// </summary>
    /// <remarks>
    /// <c>Product.IsActive</c> n'est consulté qu'à l'entrée d'une vente
    /// (<c>RegisterSaleUseCase.AcquireProductsAsync</c>) : ni <c>AdvanceOrderStatusUseCase</c> ni le décrément de
    /// fabrication ne le lisent. Ce scénario fige donc les deux faces de cette décision — le parcours en cours
    /// se termine, et la porte d'entrée se ferme.
    /// </remarks>
    [Fact]
    public async Task ProduitDesactiveApresVente_ConserveLHistorique_EtNeBloquePasLeParcoursEnCours()
    {
        var parcours = await MonterParcoursJusquEnFabricationAsync("s8.db");

        // --- Acte : désactiver la monture, déjà vendue et déjà sortie du stock.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var deactivated = await scope.SetProductActive.ExecuteAsync(new SetProductActiveCommand
            {
                ProductId = parcours.FrameId,
                IsActive = false
            });
            deactivated.ProductFound.Should().BeTrue();
        }

        // --- Acte : poursuivre le parcours engagé jusqu'à la livraison, puis régler.
        await MenerJusquALaLivraisonAsync(parcours);

        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var settle = await scope.SettleOrderBalance.ExecuteAsync(
                new SettleOrderBalanceCommand { OrderId = parcours.OrderId });
            settle.AlreadySettled.Should().BeFalse("la désactivation d'un produit ne bloque pas l'encaissement");
            settle.AmountEncashed.Should().Be(AcceptanceData.ExpectedRemaining);
        }

        // --- Acte : tenter une NOUVELLE vente du produit désactivé.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var act = () => scope.RegisterSale.ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = parcours.CustomerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new List<RegisterSaleLineCommand>
                {
                    new()
                    {
                        ProductId = parcours.FrameId,
                        ItemType = OrderItemType.Frame,
                        Quantity = 1,
                        UnitPrice = AcceptanceData.FramePrice
                    }
                }
            });

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
            thrown.Which.Message.Should().Be(RegisterSaleUseCase.ProductInactiveMessage);
        }

        // --- Assert : historique intégralement conservé, lu depuis un contexte neuf.
        await using var read = OpenFreshRead(parcours.DatabasePath);

        var frame = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == parcours.FrameId);
        frame.IsActive.Should().BeFalse("le produit est désactivé, jamais supprimé");
        frame.StockQuantity.Should().Be(AcceptanceData.FrameInitialStock - 1,
            "la vente refusée n'a pas décrémenté à nouveau la monture");

        // La ligne de vente historique reste rattachée au produit désactivé.
        var saleItems = await read.SaleItems.AsNoTracking().Where(i => i.SaleId == parcours.SaleId).ToListAsync();
        saleItems.Should().HaveCount(3, "aucune ligne d'historique n'est perdue");
        saleItems.Should().Contain(i => i.ProductId == parcours.FrameId,
            "la ligne conserve sa clé étrangère vers le produit désactivé");

        // Le parcours engagé s'est bien achevé.
        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.OrderId == parcours.OrderId);
        order.Status.Should().Be(OrderStatus.Delivered);

        var sale = await read.Sales.AsNoTracking().SingleAsync(s => s.SaleId == parcours.SaleId);
        sale.PaymentStatus.Should().Be(PaymentStatus.Paid);
        sale.RemainingAmount.Should().Be(0m);

        // Mouvements et fiche atelier intacts : 1 à la vente (monture) + 2 à la fabrication (verres).
        (await read.StockMovements.CountAsync()).Should().Be(3, "aucun mouvement historique n'est perdu");

        var sheets = await read.WorkshopSheets.AsNoTracking()
            .Where(s => s.OrderId == parcours.OrderId)
            .ToListAsync();
        sheets.Should().HaveCount(1);
        sheets.Single().QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
    }
}
