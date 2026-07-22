using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Constants;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Application.Tests.Acceptance;

/// <summary>
/// Scénarios S4, S5 et S6 (P3-11) — le cœur transversal du parcours : rollback de fabrication, contrôle qualité
/// bloquant, et idempotence d'une transition rejouée.
/// </summary>
public sealed class FabricationAndQualityAcceptanceTests : AcceptanceScenarioBase
{
    /// <summary>Contexte d'un parcours amené jusqu'à un statut donné, exclusivement par use cases publics.</summary>
    private sealed record Parcours(
        string DatabasePath,
        long FrameId,
        long LensOdId,
        long LensOgId,
        long CustomerId,
        long SaleId,
        long OrderId,
        string OrderNumber);

    /// <summary>
    /// Monte le parcours nominal jusqu'à <c>ToFabricate</c> : fournisseur, produits, client, vente, puis la
    /// première transition. Chaque acte passe par un use case public, dans sa propre portée.
    /// </summary>
    private async Task<Parcours> MonterParcoursJusquALaFabricationAsync(string fileName)
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
            sale.OrderId.Should().NotBeNull();
            orderId = sale.OrderId!.Value;
            orderNumber = sale.OrderNumber!;
        }

        var toFabricate = await AcceptanceActs.AvancerStatutAsync(
            databasePath, orderId, OrderStatus.New, OrderStatus.ToFabricate);
        toFabricate.NewStatus.Should().Be(OrderStatus.ToFabricate);

        return new Parcours(databasePath, frameId, lensOdId, lensOgId, customerId, saleId, orderId, orderNumber);
    }

    // =========================================================================================================
    // S4 — rollback transversal de l'entrée en fabrication.
    // =========================================================================================================

    /// <summary>
    /// S4 — stock d'un verre insuffisant à l'entrée en fabrication : <b>tout</b> est annulé — statut, fiche
    /// atelier, décrément du second verre, mouvements et notification.
    /// </summary>
    /// <remarks>
    /// C'est le seul rollback réellement multi-domaine du parcours. Les tests ciblés prouvent le rollback du
    /// stock ; aucun ne l'a jamais prouvé <b>en présence de la fiche atelier</b>, qui est générée dans la même
    /// transaction, ni conjointement avec la notification.
    /// </remarks>
    [Fact]
    public async Task FabricationAvecStockVerreInsuffisant_AnnuleStatutFicheStockEtNotification()
    {
        var parcours = await MonterParcoursJusquALaFabricationAsync("s4.db");

        // --- Acte : ramener le stock du verre OG à zéro (ajustement d'inventaire, use case public).
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var adjustment = await scope.CreateStockMovement.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = parcours.LensOgId,
                MovementType = StockMovementType.Adjustment,
                Quantity = 0,
                Reason = "Inventaire : verre OG épuisé"
            });
            adjustment.ProductFound.Should().BeTrue();
            adjustment.NewStockQuantity.Should().Be(0);
        }

        // --- Acte : tenter l'entrée en fabrication. Le verre OD sera décrémenté avant que l'OG n'échoue :
        // c'est précisément ce décrément partiel que le rollback doit effacer.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var act = () => scope.AdvanceOrderStatus.ExecuteAsync(
                AcceptanceActs.Transition(parcours.OrderId, OrderStatus.ToFabricate, OrderStatus.InProgress));

            await act.Should().ThrowAsync<InsufficientStockException>(
                "un verre à zéro ne peut pas entrer en fabrication");
        }

        // --- Assert : état relu depuis un contexte NEUF.
        await using var read = OpenFreshRead(parcours.DatabasePath);

        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.OrderId == parcours.OrderId);
        order.Status.Should().Be(OrderStatus.ToFabricate, "le statut ne doit pas avoir avancé");

        (await read.WorkshopSheets.CountAsync()).Should().Be(0,
            "la fiche atelier est générée dans la même transaction : elle doit être annulée avec elle");
        (await read.WorkshopSheetItems.CountAsync()).Should().Be(0);

        var lensOd = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == parcours.LensOdId);
        lensOd.StockQuantity.Should().Be(AcceptanceData.LensInitialStock,
            "le décrément partiel du verre OD doit être intégralement annulé");

        var lensOg = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == parcours.LensOgId);
        lensOg.StockQuantity.Should().Be(0, "l'ajustement d'inventaire, lui, était une écriture distincte et acquise");

        var fabricationMovements = await read.StockMovements.AsNoTracking()
            .Where(m => m.Reason != null && m.Reason.Contains(parcours.OrderNumber))
            .ToListAsync();
        fabricationMovements.Should().BeEmpty("aucun mouvement de fabrication, même partiel");

        var notifications = await read.Notifications.AsNoTracking().ToListAsync();
        notifications.Should().HaveCount(1,
            "seule la notification de New → ToFabricate subsiste ; celle de l'entrée en fabrication est annulée");
        notifications.Single().Type.Should().Be(NotificationTypes.OrderStatusChanged);
    }

    // =========================================================================================================
    // S5 — contrôle qualité bloquant, puis débloquant.
    // =========================================================================================================

    /// <summary>
    /// S5 — <c>Ready</c> est refusé tant que le contrôle qualité n'est pas validé, puis autorisé après
    /// validation, sur le <b>même</b> parcours.
    /// </summary>
    [Fact]
    public async Task PassageEnPret_EstBloqueSansControleQualite_PuisAutoriseApresValidation()
    {
        var parcours = await MonterParcoursJusquALaFabricationAsync("s5.db");

        await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.InProgress, OrderStatus.QualityCheck);

        // Trois transitions réussies ⇒ trois notifications, avant toute tentative de passage en « prêt ».
        int notificationsAvantRefus;
        await using (var read = OpenFreshRead(parcours.DatabasePath))
        {
            notificationsAvantRefus = await read.Notifications.CountAsync();
            notificationsAvantRefus.Should().Be(3);
        }

        // --- Acte : tenter Ready SANS contrôle qualité validé.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var act = () => scope.AdvanceOrderStatus.ExecuteAsync(
                AcceptanceActs.Transition(parcours.OrderId, OrderStatus.QualityCheck, OrderStatus.Ready));

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
            thrown.Which.Message.Should().Be(WorkshopSheetPolicy.ReadyRequiresPassedQcMessage);
        }

        await using (var read = OpenFreshRead(parcours.DatabasePath))
        {
            var refused = await read.Orders.AsNoTracking().SingleAsync(o => o.OrderId == parcours.OrderId);
            refused.Status.Should().Be(OrderStatus.QualityCheck, "un refus ne fait pas avancer le statut");

            (await read.Notifications.CountAsync()).Should().Be(notificationsAvantRefus,
                "un refus n'ajoute aucune notification");
        }

        // --- Acte : valider le contrôle qualité.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var current = await scope.GetCurrentWorkshopSheet.ExecuteAsync(
                new GetCurrentWorkshopSheetQuery { OrderId = parcours.OrderId });
            current.SheetFound.Should().BeTrue("la fiche a été générée à l'entrée en fabrication");

            var qc = await scope.ValidateWorkshopSheetQc.ExecuteAsync(new ValidateWorkshopSheetQcCommand
            {
                WorkshopSheetId = current.Sheet!.WorkshopSheetId,
                Comment = "Montage conforme"
            });
            qc.SheetFound.Should().BeTrue();
            qc.Sheet!.QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
        }

        // --- Acte : retenter Ready, désormais autorisé.
        var ready = await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.QualityCheck, OrderStatus.Ready);
        ready.NewStatus.Should().Be(OrderStatus.Ready);

        await using var finalRead = OpenFreshRead(parcours.DatabasePath);
        var order = await finalRead.Orders.AsNoTracking().SingleAsync(o => o.OrderId == parcours.OrderId);
        order.Status.Should().Be(OrderStatus.Ready);

        (await finalRead.Notifications.CountAsync()).Should().Be(notificationsAvantRefus + 1,
            "la transition réussie ajoute exactement une notification ; le refus n'en avait ajouté aucune");
    }

    // =========================================================================================================
    // S6 — rejeu d'une transition déjà appliquée.
    // =========================================================================================================

    /// <summary>
    /// S6 — rejouer <c>ToFabricate → InProgress</c> depuis un <b>second contexte</b> ne produit ni second
    /// décrément, ni seconde fiche, ni seconde notification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C'est le seul rejeu capable de produire un <b>double décrément de stock verres</b>. Les tests ciblés
    /// prouvent que le conflit est levé ; aucun ne prouve que les effets <b>cumulés</b> restent inchangés.
    /// </para>
    /// <para>
    /// Le rejeu s'effectue dans une portée neuve, comme le ferait un second poste ou un second clic. Enchaîner
    /// deux transitions dans un même <c>DbContext</c> n'est volontairement pas tenté : cette limite est une dette
    /// documentée du modèle (rapport P3-11), et non l'objet de ce scénario.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TransitionRejouee_NeDecrementePasDeuxFoisLeStock_EtNeCreeNiSecondeFicheNiSecondeNotification()
    {
        var parcours = await MonterParcoursJusquALaFabricationAsync("s6.db");

        var inProgress = await AcceptanceActs.AvancerStatutAsync(
            parcours.DatabasePath, parcours.OrderId, OrderStatus.ToFabricate, OrderStatus.InProgress);
        inProgress.HasCreatedWorkshopSheet.Should().BeTrue();
        inProgress.CreatedStockMovementCount.Should().Be(2);

        // --- Acte : REJOUER exactement la même transition, depuis une portée neuve.
        await using (var scope = OpenScope(parcours.DatabasePath))
        {
            var act = () => scope.AdvanceOrderStatus.ExecuteAsync(
                AcceptanceActs.Transition(parcours.OrderId, OrderStatus.ToFabricate, OrderStatus.InProgress));

            await act.Should().ThrowAsync<OrderStatusConflictException>(
                "la prise atomique refuse une transition dont le statut attendu n'est plus celui stocké");
        }

        // --- Assert : les effets CUMULÉS sont strictement ceux de la première exécution.
        await using var read = OpenFreshRead(parcours.DatabasePath);

        var order = await read.Orders.AsNoTracking().SingleAsync(o => o.OrderId == parcours.OrderId);
        order.Status.Should().Be(OrderStatus.InProgress);

        var lensOd = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == parcours.LensOdId);
        var lensOg = await read.Products.AsNoTracking().SingleAsync(p => p.ProductId == parcours.LensOgId);
        lensOd.StockQuantity.Should().Be(AcceptanceData.LensInitialStock - 1, "un seul décrément, pas deux");
        lensOg.StockQuantity.Should().Be(AcceptanceData.LensInitialStock - 1, "un seul décrément, pas deux");

        var fabricationMovements = await read.StockMovements.AsNoTracking()
            .Where(m => m.Reason != null && m.Reason.Contains(parcours.OrderNumber))
            .ToListAsync();
        fabricationMovements.Should().HaveCount(2, "deux verres, deux mouvements — jamais quatre");

        var sheets = await read.WorkshopSheets.AsNoTracking()
            .Where(s => s.OrderId == parcours.OrderId)
            .ToListAsync();
        sheets.Should().HaveCount(1, "aucune seconde fiche n'est créée par un rejeu");
        sheets.Single().Version.Should().Be(1);
        sheets.Single().IsCurrent.Should().BeTrue();

        (await read.Notifications.CountAsync()).Should().Be(2,
            "deux transitions réussies ⇒ deux notifications ; le rejeu refusé n'en ajoute aucune");
    }
}
