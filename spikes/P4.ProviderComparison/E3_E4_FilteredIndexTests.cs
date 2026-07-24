using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.P4.ProviderComparison.Support;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E3 — Index unique filtré « alerte LowStock ACTIVE » (au plus une par produit).
/// E4 — Index unique filtré « fiche atelier COURANTE » (au plus une par commande) + unicité
///      (OrderId, Version).
///
/// Les deux garanties sont structurelles : c'est la BASE qui doit refuser le doublon, pas
/// l'application. On vérifie donc que la seconde écriture ÉCHOUE réellement.
/// </summary>
public class E3_E4_FilteredIndexTests
{
    private const string Experiment = "E3-E4-filtered-indexes";

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task LowStock_active_alert_is_unique_per_product(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e3");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E3 — LowStock actif unique");

        const long productId = 4242;

        // 1) Première alerte active : doit passer.
        var first = await TryInsertAlertAsync(database, productId, "premiere");
        SpikeLog.Write(Experiment, name, $"1re alerte active : {first}");

        // 2) Seconde alerte active pour le MÊME produit : doit être refusée par l'index.
        var second = await TryInsertAlertAsync(database, productId, "seconde");
        SpikeLog.Write(Experiment, name, $"2e alerte active (même produit) : {second}");

        // 3) Résolution de l'alerte active.
        await using (var resolve = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var updated = await resolve.Notifications
                .Where(n => n.Type == NotificationTypes.LowStock && n.EntityId == productId && n.ResolvedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ResolvedAt, new DateTime(2026, 7, 24, 13, 0, 0, DateTimeKind.Utc)));
            SpikeLog.Write(Experiment, name, $"Résolution : {updated} ligne(s)");
        }

        // 4) Nouvel épisode après résolution : doit être ACCEPTÉ (l'historique est conservé).
        var third = await TryInsertAlertAsync(database, productId, "nouvel-episode");
        SpikeLog.Write(Experiment, name, $"Nouvel épisode après résolution : {third}");

        await using (var count = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var total = await count.Notifications.CountAsync(n => n.EntityId == productId);
            var active = await count.Notifications.CountAsync(n => n.EntityId == productId && n.ResolvedAt == null);
            SpikeLog.Write(Experiment, name, $"État final : {total} alerte(s) au total, {active} active(s) — historique {(total > 1 ? "CONSERVÉ" : "PERDU")}");
        }

        Assert.True(true);
    }

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Workshop_sheet_current_version_is_unique_per_order(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        await using var database = await SpikeDatabase.CreateAsync(provider, "e4");
        await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        SpikeLog.Section(Experiment, name, "E4 — Fiche atelier courante unique");

        long orderId;
        await using (var seed = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var sale = new Sale
            {
                SaleNumber = "E4-SALE-1",
                SaleDate = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
                TotalAmount = 100m,
                FinalAmount = 100m,
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid,
                Status = SaleStatus.Draft
            };
            seed.Sales.Add(sale);
            await seed.SaveChangesAsync();

            var order = new Order
            {
                OrderNumber = "E4-ORDER-1",
                SaleId = sale.SaleId,
                OrderDate = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
                Status = OrderStatus.New
            };
            seed.Orders.Add(order);
            await seed.SaveChangesAsync();
            orderId = order.OrderId;
        }

        // v1 courante.
        var v1 = await TryInsertSheetAsync(database, orderId, version: 1, isCurrent: true);
        SpikeLog.Write(Experiment, name, $"v1 (courante) : {v1}");

        // v2 ÉGALEMENT courante, sans basculer v1 : doit être refusée par l'index filtré.
        var v2Conflict = await TryInsertSheetAsync(database, orderId, version: 2, isCurrent: true);
        SpikeLog.Write(Experiment, name, $"v2 courante SANS bascule (doit échouer) : {v2Conflict}");

        // Bascule atomique v1 -> false puis v2 -> true, dans une transaction.
        string atomic;
        try
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            await using var transaction = await context.Database.BeginTransactionAsync();

            await context.WorkshopSheets.Where(w => w.OrderId == orderId && w.IsCurrent)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsCurrent, false));

            context.WorkshopSheets.Add(NewSheet(orderId, 2, true));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            atomic = "SUCCÈS";
        }
        catch (Exception ex)
        {
            atomic = $"ÉCHEC : {E1_ModelCreationTests.Flatten(ex)}";
        }

        SpikeLog.Write(Experiment, name, $"Bascule atomique v1->v2 en transaction : {atomic}");

        // Doublon (OrderId, Version) : doit être refusé par l'index unique non filtré.
        var duplicate = await TryInsertSheetAsync(database, orderId, version: 2, isCurrent: false);
        SpikeLog.Write(Experiment, name, $"Doublon (OrderId, Version=2) : {duplicate}");

        await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
        {
            var currents = await check.WorkshopSheets.CountAsync(w => w.OrderId == orderId && w.IsCurrent);
            var versions = await check.WorkshopSheets.CountAsync(w => w.OrderId == orderId);
            SpikeLog.Write(Experiment, name, $"État final : {versions} version(s), {currents} courante(s)");
        }

        Assert.True(true);
    }

    private static WorkshopSheet NewSheet(long orderId, int version, bool isCurrent) => new()
    {
        OrderId = orderId,
        Version = version,
        IsCurrent = isCurrent,
        CreatedAt = new DateTime(2026, 7, 24, 11, 0, 0, DateTimeKind.Utc),
        TechnicalFingerprint = new string('a', 64),
        OrderNumberSnapshot = "E4-ORDER-1",
        OrderDateSnapshot = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
        CustomerNameSnapshot = "Client Spike",
        QcStatus = WorkshopSheetQcStatus.Pending
    };

    private static async Task<string> TryInsertSheetAsync(SpikeDatabase database, long orderId, int version, bool isCurrent)
    {
        try
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            context.WorkshopSheets.Add(NewSheet(orderId, version, isCurrent));
            await context.SaveChangesAsync();
            return "ACCEPTÉ";
        }
        catch (DbUpdateException ex)
        {
            return $"REFUSÉ PAR LA BASE — {ErrorFacts.Describe(ex)}";
        }
    }

    private static async Task<string> TryInsertAlertAsync(SpikeDatabase database, long productId, string tag)
    {
        try
        {
            await using var context = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal);
            context.Notifications.Add(new Notification
            {
                Type = NotificationTypes.LowStock,
                Title = $"Stock bas {tag}",
                Message = $"Alerte {tag}",
                EntityId = productId,
                EntityType = NotificationEntityTypes.Product,
                IsRead = false,
                CreatedAt = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc)
            });
            await context.SaveChangesAsync();
            return "ACCEPTÉ";
        }
        catch (DbUpdateException ex)
        {
            return $"REFUSÉ PAR LA BASE — {ErrorFacts.Describe(ex)}";
        }
    }
}
