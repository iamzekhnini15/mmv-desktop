using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Policies;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Primitives;

/// <summary>
/// N6 — re-preuve des 14 primitives (matrice P4-1 Lot B §6.1) sur PostgreSQL, via leurs implémentations de
/// PRODUCTION (O12, critère 5). Aucune n'est réimplémentée ici.
/// <para>
/// Portée : preuve CI Linux (conteneur PostgreSQL) ; la preuve contre l'installation Windows native exigée par
/// O12 reste séparée (arbitrage A2). Concurrence multi-processus hors périmètre (ADR-PROD-DB-008 §5.11, P4-10).
/// </para>
/// </summary>
public class PrimitivesTests : PostgreSqlTestBase
{
    // ---------- 1 · 2 · 3 — stock ----------

    [PostgreSqlFact]
    public async Task P01_decrement_is_conditional_on_available_stock()
    {
        var productId = await ProductAsync(stock: 10);

        await using (var context = NewContext())
        {
            var stock = new EfStockMutationService(context);
            await stock.DecrementStockAsync(productId, 3);

            var act = () => stock.DecrementStockAsync(productId, 8);
            (await act.Should().ThrowAsync<InsufficientStockException>()).Which.Message.Should().NotBeNullOrEmpty();
        }

        (await StockOfAsync(productId)).Should().Be(7);
    }

    [PostgreSqlFact]
    public async Task P02_increment_returns_the_new_quantity()
    {
        var productId = await ProductAsync(stock: 10);

        await using var context = NewContext();
        (await new EfStockMutationService(context).IncrementStockAsync(productId, 5)).Should().Be(15);
        (await StockOfAsync(productId)).Should().Be(15);
    }

    [PostgreSqlFact]
    public async Task P03_adjustment_sets_the_target_and_reports_the_previous_quantity()
    {
        var productId = await ProductAsync(stock: 10);

        await using var context = NewContext();
        var result = await new EfStockMutationService(context).AdjustStockToAsync(productId, 4);

        result.PreviousQuantity.Should().Be(10);
        (await StockOfAsync(productId)).Should().Be(4);
    }

    // ---------- 4 · 5 — transaction de vente composée, numérotation ----------

    [PostgreSqlFact]
    public async Task P04_composed_sale_transaction_rolls_back_every_write_on_failure()
    {
        var productId = await ProductAsync(stock: 1);

        await using (var context = NewContext())
        {
            var act = () => new EfTransactionRunner(context).RunAsync(async ct =>
            {
                var number = await new EfNumberSequenceService(context).NextNumberAsync("SALE", ct);
                context.Sales.Add(new Sale { SaleNumber = number, FinalAmount = 60m });
                await context.SaveChangesAsync(ct);
                await new EfStockMutationService(context).DecrementStockAsync(productId, 2, ct); // stock insuffisant
            });

            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        await using var verify = NewContext();
        (await verify.Sales.CountAsync()).Should().Be(0);
        (await verify.DocumentSequences.SingleAsync(s => s.SequenceName == "SALE")).CurrentValue.Should().Be(0);
        (await StockOfAsync(productId)).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task P04_composed_sale_transaction_commits_every_write_on_success()
    {
        var productId = await ProductAsync(stock: 1);

        await using (var context = NewContext())
        {
            await new EfTransactionRunner(context).RunAsync(async ct =>
            {
                var number = await new EfNumberSequenceService(context).NextNumberAsync("SALE", ct);
                context.Sales.Add(new Sale { SaleNumber = number, FinalAmount = 60m });
                await context.SaveChangesAsync(ct);
                await new EfStockMutationService(context).DecrementStockAsync(productId, 1, ct);
            });
        }

        await using var verify = NewContext();
        (await verify.Sales.SingleAsync()).SaleNumber.Should().Be("VTE-000001");
        (await StockOfAsync(productId)).Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task P05_document_numbers_are_sequential_from_the_migrated_seed()
    {
        await using var context = NewContext();
        var sequence = new EfNumberSequenceService(context);

        (await sequence.NextNumberAsync("ORDER")).Should().Be("CMD-000001");
        (await sequence.NextNumberAsync("ORDER")).Should().Be("CMD-000002");
        (await sequence.NextNumberAsync("SALE")).Should().Be("VTE-000001");
    }

    // ---------- 6 · 7 · 8 · 9 — commande et fiche atelier ----------

    [PostgreSqlFact]
    public async Task P06_status_transition_is_taken_once_from_the_expected_status()
    {
        var orderId = await OrderAsync(OrderStatus.New);

        await using var context = NewContext();
        var orders = new OrderRepository(context);
        (await orders.TryTransitionStatusAsync(orderId, OrderStatus.New, OrderStatus.ToFabricate)).Should().BeTrue();
        (await orders.TryTransitionStatusAsync(orderId, OrderStatus.New, OrderStatus.ToFabricate)).Should().BeFalse();
        (await StatusOfAsync(orderId)).Should().Be(OrderStatus.ToFabricate);
    }

    [PostgreSqlFact]
    public async Task P07_ready_transition_requires_the_current_passed_sheet()
    {
        var orderId = await OrderAsync(OrderStatus.QualityCheck);
        var sheet = await CreateSheetAsync(orderId);
        await TakeQcAsync(sheet.WorkshopSheetId, WorkshopSheetQcStatus.Passed);

        await using var context = NewContext();
        var orders = new OrderRepository(context);
        (await orders.TryTransitionWithWorkshopSheetAsync(
                orderId, OrderStatus.QualityCheck, OrderStatus.Ready, sheet.WorkshopSheetId, "empreinte-perimee"))
            .Should().Be(OrderReadyTransitionOutcome.WorkshopSheetRequirementNotMet);
        (await orders.TryTransitionWithWorkshopSheetAsync(
                orderId, OrderStatus.QualityCheck, OrderStatus.Ready, sheet.WorkshopSheetId, sheet.TechnicalFingerprint))
            .Should().Be(OrderReadyTransitionOutcome.Taken);
        (await orders.TryTransitionWithWorkshopSheetAsync(
                orderId, OrderStatus.QualityCheck, OrderStatus.Ready, sheet.WorkshopSheetId, sheet.TechnicalFingerprint))
            .Should().Be(OrderReadyTransitionOutcome.StatusConflict);
        (await StatusOfAsync(orderId)).Should().Be(OrderStatus.Ready);
    }

    [PostgreSqlFact]
    public async Task P08_next_sheet_version_is_guarded_by_its_precondition()
    {
        var orderId = await OrderAsync(OrderStatus.InProgress);
        var v1 = await CreateSheetAsync(orderId);
        var staleView = WorkshopSheetVersionPrecondition.From(v1);

        (await CreateSheetAsync(orderId, staleView)).Version.Should().Be(2);
        var act = () => CreateSheetAsync(orderId, staleView);
        await act.Should().ThrowAsync<WorkshopSheetVersionConflictException>();

        await using var verify = NewContext();
        var versions = await verify.WorkshopSheets.Where(w => w.OrderId == orderId).OrderBy(w => w.Version).ToListAsync();
        versions.Select(v => (v.Version, v.IsCurrent)).Should().Equal((1, false), (2, true));
    }

    [PostgreSqlFact]
    public async Task P09_qc_decision_is_definitive()
    {
        var orderId = await OrderAsync(OrderStatus.QualityCheck);
        var sheet = await CreateSheetAsync(orderId);

        (await TakeQcAsync(sheet.WorkshopSheetId, WorkshopSheetQcStatus.Passed)).Should().BeTrue();
        (await TakeQcAsync(sheet.WorkshopSheetId, WorkshopSheetQcStatus.Failed)).Should().BeFalse();

        await using var verify = NewContext();
        (await verify.WorkshopSheets.SingleAsync()).QcStatus.Should().Be(WorkshopSheetQcStatus.Passed);
    }

    // ---------- 10 — règlement du solde (sémantique monétaire détaillée en N3/M6) ----------

    [PostgreSqlFact]
    public async Task P10_balance_settlement_succeeds_once()
    {
        long saleId;
        await using (var context = NewContext())
        {
            var sale = new Sale { SaleNumber = "V-1", FinalAmount = 80m, DepositAmount = 30m, RemainingAmount = 50m };
            context.Sales.Add(sale);
            await context.SaveChangesAsync();
            saleId = sale.SaleId;
        }

        await using var act = NewContext();
        var sales = new SaleRepository(act);
        (await sales.TrySettleRemainingBalanceAsync(saleId)).Should().BeTrue();
        (await sales.TrySettleRemainingBalanceAsync(saleId)).Should().BeFalse();
    }

    // ---------- 11 · 12 — notifications de stock bas ----------

    [PostgreSqlFact]
    public async Task P11_active_low_stock_alert_is_created_once()
    {
        var productId = await ProductAsync(stock: 1);

        await using (var context = NewContext())
        {
            var notifications = new NotificationRepository(context);
            (await notifications.TryCreateActiveLowStockAsync(LowStock(productId))).Should().BeTrue();
            (await notifications.TryCreateActiveLowStockAsync(LowStock(productId))).Should().BeFalse();
        }

        await using var verify = NewContext();
        (await verify.Notifications.CountAsync(n => n.EntityId == productId && n.ResolvedAt == null)).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task P12_reconciliation_resolves_active_alerts_and_allows_a_new_one()
    {
        var productId = await ProductAsync(stock: 1);

        await using var context = NewContext();
        var notifications = new NotificationRepository(context);
        await notifications.TryCreateActiveLowStockAsync(LowStock(productId));

        (await notifications.ResolveActiveLowStockAsync(new[] { productId }, DateTime.UtcNow)).Should().Be(1);
        (await notifications.ResolveActiveLowStockAsync(new[] { productId }, DateTime.UtcNow)).Should().Be(0);
        (await notifications.GetActiveLowStockEntityIdsAsync()).Should().BeEmpty();
        (await notifications.TryCreateActiveLowStockAsync(LowStock(productId))).Should().BeTrue();
    }

    // ---------- 13 · 14 — fournisseur, unicité du login ----------

    [PostgreSqlFact]
    public async Task P13_supplier_is_deleted_only_when_unused()
    {
        long used, unused;
        await using (var context = NewContext())
        {
            used = await Arrange.SupplierAsync(context, "Utilisé");
            await Arrange.ProductAsync(context, used);
            unused = await Arrange.SupplierAsync(context, "Libre");
        }

        await using var act = NewContext();
        var suppliers = new SupplierRepository(act);
        (await suppliers.TryDeleteIfUnusedAsync(used)).Should().BeFalse();
        (await suppliers.TryDeleteIfUnusedAsync(unused)).Should().BeTrue();
        (await suppliers.ExistsFreshAsync(used)).Should().BeTrue();
        (await suppliers.ExistsFreshAsync(unused)).Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task P14_normalized_username_is_unique_and_mapped_as_unique_constraint()
    {
        await AddUserAsync("Admin");

        var act = () => AddUserAsync("ADMIN");

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        PersistenceErrorMapper.Map(thrown).Should().BeOfType<PersistenceException>()
            .Which.Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
    }

    // ---------- arrangement ----------

    private async Task<long> ProductAsync(int stock)
    {
        await using var context = NewContext();
        return await Arrange.ProductAsync(context, await Arrange.SupplierAsync(context), stock: stock);
    }

    private async Task<long> OrderAsync(OrderStatus status)
    {
        await using var context = NewContext();
        return await Arrange.OrderAsync(context, status);
    }

    private async Task<WorkshopSheet> CreateSheetAsync(long orderId, WorkshopSheetVersionPrecondition? precondition = null)
    {
        await using var context = NewContext();
        var orders = new OrderRepository(context);
        var order = await orders.GetWithItemsAsync(orderId);
        return await orders.CreateNextWorkshopSheetVersionAsync(
            order!, precondition ?? WorkshopSheetVersionPrecondition.None, DateTime.UtcNow);
    }

    private async Task<bool> TakeQcAsync(long sheetId, WorkshopSheetQcStatus decision)
    {
        await using var context = NewContext();
        return await new OrderRepository(context).TryTakeWorkshopSheetQcDecisionAsync(sheetId, decision, "QC", DateTime.UtcNow);
    }

    private async Task AddUserAsync(string username)
    {
        await using var context = NewContext();
        context.Users.Add(new User
        {
            Username = username,
            NormalizedUsername = UserIdentityPolicy.NormalizeUsername(username),
            PasswordHash = "hash",
            FirstName = "Test",
            LastName = "Utilisateur",
            Role = UserRole.Admin
        });
        await context.SaveChangesAsync();
    }

    private async Task<int> StockOfAsync(long productId)
    {
        await using var context = NewContext();
        return (await context.Products.SingleAsync(p => p.ProductId == productId)).StockQuantity;
    }

    private async Task<OrderStatus> StatusOfAsync(long orderId)
    {
        await using var context = NewContext();
        return (await context.Orders.SingleAsync(o => o.OrderId == orderId)).Status;
    }

    private static Notification LowStock(long productId) => new()
    {
        Type = NotificationTypes.LowStock,
        EntityType = NotificationEntityTypes.Product,
        EntityId = productId,
        Title = "Stock bas",
        Message = "Stock bas",
        CreatedAt = DateTime.UtcNow
    };
}
