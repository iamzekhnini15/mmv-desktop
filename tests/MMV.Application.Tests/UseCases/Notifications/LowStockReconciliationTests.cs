using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Notifications.CountUnreadNotifications;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P3-8 — Règles métier de la réconciliation des alertes de stock bas, sur <b>vraie base SQLite jetable</b>.
///
/// <para>
/// Le fil directeur de ces tests est la séparation des <b>deux axes d'état</b> que l'implémentation d'origine
/// confondait : <c>IsRead</c> (« l'opérateur a vu ») et <c>ResolvedAt</c> (« la condition est terminée »). Chacun des
/// quatre quadrants doit être atteignable, et seul le second doit décider de l'anti-doublon.
/// </para>
/// </summary>
public sealed class LowStockReconciliationTests : IDisposable
{
    private readonly string _workDirectory;

    public LowStockReconciliationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p38-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Harnais
    // ---------------------------------------------------------------------------------------------------------

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>Crée la base et un produit unique, et renvoie son identifiant.</summary>
    private async Task<(string DbPath, long ProductId)> SeedSingleProductAsync(
        string fileName, int stock, int threshold, bool isActive = true)
    {
        var dbPath = PathFor(fileName);
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var supplier = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "S" });
        await new UnitOfWork(context).SaveChangesAsync();

        var product = await new ProductRepository(context).CreateAsync(new Product
        {
            Reference = "REF-1",
            Name = "Produit",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            StockQuantity = stock,
            StockAlertThreshold = threshold,
            IsActive = isActive,
        });
        await new UnitOfWork(context).SaveChangesAsync();

        return (dbPath, product.ProductId);
    }

    /// <summary>Exécute une réconciliation complète sur une portée neuve (comme en production).</summary>
    private static async Task<GenerateLowStockNotificationsResult> ReconcileAsync(string dbPath)
    {
        using var context = CreateContext(dbPath);
        var useCase = new GenerateLowStockNotificationsUseCase(
            new ProductRepository(context),
            new NotificationRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context));
        return await useCase.ExecuteAsync();
    }

    private static async Task MarkAllReadAsync(string dbPath)
    {
        using var context = CreateContext(dbPath);
        await new MarkAllNotificationsReadUseCase(new NotificationRepository(context), new UnitOfWork(context))
            .ExecuteAsync();
    }

    /// <summary>Applique une mutation directe au produit (stock, seuil, activité) hors du flux d'alerte.</summary>
    private static async Task MutateProductAsync(string dbPath, long productId, Action<Product> mutate)
    {
        using var context = CreateContext(dbPath);
        var product = await context.Products.FirstAsync(p => p.ProductId == productId);
        mutate(product);
        await context.SaveChangesAsync();
    }

    private static async Task<List<Notification>> LowStockAlertsAsync(string dbPath, long productId)
    {
        using var context = CreateContext(dbPath);
        return await context.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationTypes.LowStock && n.EntityId == productId)
            .OrderBy(n => n.NotificationId)
            .ToListAsync();
    }

    // =========================================================================================================
    // Génération et anti-doublon
    // =========================================================================================================

    [Fact]
    public async Task ProduitSousSeuil_CreeUneAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("under.db", stock: 1, threshold: 5);

        var result = await ReconcileAsync(dbPath);

        result.CreatedCount.Should().Be(1);
        result.ResolvedCount.Should().Be(0);
        (await LowStockAlertsAsync(dbPath, productId)).Should().ContainSingle();
    }

    [Fact]
    public async Task StockExactementEgalAuSeuil_CreeUneAlerte()
    {
        // Fige la frontière : la comparaison est INCLUSIVE (<=) depuis l'origine. Un refactor qui la rendrait
        // stricte ferait taire l'alerte exactement au seuil, silencieusement.
        var (dbPath, productId) = await SeedSingleProductAsync("equal.db", stock: 5, threshold: 5);

        var result = await ReconcileAsync(dbPath);

        result.CreatedCount.Should().Be(1);
        (await LowStockAlertsAsync(dbPath, productId)).Should().ContainSingle();
    }

    [Fact]
    public async Task StockZero_CreeUneSeuleAlerteDeTypeLowStock()
    {
        // Le runtime n'émet JAMAIS « StockOut » : un stock nul satisfait 0 <= seuil et produit un LowStock.
        // « StockOut » est un type orphelin de seed (documenté), jamais généré ni lu.
        var (dbPath, productId) = await SeedSingleProductAsync("zero.db", stock: 0, threshold: 5);

        await ReconcileAsync(dbPath);

        var alerts = await LowStockAlertsAsync(dbPath, productId);
        alerts.Should().ContainSingle();
        alerts[0].Type.Should().Be(NotificationTypes.LowStock);

        using var context = CreateContext(dbPath);
        context.Notifications.AsNoTracking().Count(n => n.Type == NotificationTypes.StockOut).Should().Be(0);
    }

    [Fact]
    public async Task ProduitActifAuDessusDuSeuil_NeCreeAucuneAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("above.db", stock: 20, threshold: 5);

        var result = await ReconcileAsync(dbPath);

        result.CreatedCount.Should().Be(0);
        (await LowStockAlertsAsync(dbPath, productId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ProduitInactifSousSeuil_NeCreeAucuneAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("inactive.db", stock: 1, threshold: 5, isActive: false);

        var result = await ReconcileAsync(dbPath);

        result.CreatedCount.Should().Be(0);
        (await LowStockAlertsAsync(dbPath, productId)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeuxiemeGenerationSansChangement_NeCreeRien()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("idempotent.db", stock: 1, threshold: 5);

        await ReconcileAsync(dbPath);
        var second = await ReconcileAsync(dbPath);

        second.CreatedCount.Should().Be(0);
        second.ResolvedCount.Should().Be(0);
        (await LowStockAlertsAsync(dbPath, productId)).Should().ContainSingle();
    }

    [Fact]
    public async Task GenererPuisToutMarquerCommeLuPuisGenerer_NeRecreeAucuneAlerte()
    {
        // 🔴 TEST PRIORITAIRE — reproduction du défaut central de P3-8.
        //
        // Avant correction : l'anti-doublon testait « !IsRead ». Après « tout marquer comme lu » il ne trouvait plus
        // aucune alerte NON LUE et recréait une ligne pour chaque produit encore sous seuil, à CHAQUE connexion —
        // croissance illimitée, sans la moindre concurrence. Ce test échouerait sur l'implémentation d'origine.
        var (dbPath, productId) = await SeedSingleProductAsync("markread.db", stock: 1, threshold: 5);

        await ReconcileAsync(dbPath);
        await MarkAllReadAsync(dbPath);
        var afterRead = await ReconcileAsync(dbPath);

        afterRead.CreatedCount.Should().Be(0, "lire n'est pas résoudre : la condition métier n'a pas changé");

        var alerts = await LowStockAlertsAsync(dbPath, productId);
        alerts.Should().ContainSingle();
        alerts[0].IsRead.Should().BeTrue("l'opérateur l'a bien vue");
        alerts[0].ResolvedAt.Should().BeNull("le stock est toujours bas : l'alerte reste ACTIVE");
    }

    [Fact]
    public async Task MarkAllAsRead_NeModifieJamaisResolvedAt()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("markread-axis.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);

        await MarkAllReadAsync(dbPath);

        var alerts = await LowStockAlertsAsync(dbPath, productId);
        alerts.Should().ContainSingle();
        alerts[0].ResolvedAt.Should().BeNull("MarkAllAsRead n'est autoritaire que sur l'axe de lecture");
    }

    // =========================================================================================================
    // Résolution
    // =========================================================================================================

    [Fact]
    public async Task StockRepasseAuDessusDuSeuil_ResoutLAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("restock.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);

        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);
        var result = await ReconcileAsync(dbPath);

        result.ResolvedCount.Should().Be(1);
        result.CreatedCount.Should().Be(0);

        var alerts = await LowStockAlertsAsync(dbPath, productId);
        alerts.Should().ContainSingle();
        alerts[0].ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProduitDesactive_ResoutSonAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("deactivate.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);

        await MutateProductAsync(dbPath, productId, p => p.IsActive = false);
        var result = await ReconcileAsync(dbPath);

        result.ResolvedCount.Should().Be(1, "un produit hors catalogue ne crée plus de besoin de réapprovisionnement");
        (await LowStockAlertsAsync(dbPath, productId))[0].ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProduitAbsent_ResoutSonAlerte()
    {
        // Aucune clé étrangère ne relie une notification à son produit : une alerte orpheline doit tout de même se
        // fermer, sans quoi elle resterait active à vie.
        var (dbPath, _) = await SeedSingleProductAsync("orphan.db", stock: 1, threshold: 5);

        using (var context = CreateContext(dbPath))
        {
            context.Notifications.Add(new Notification
            {
                Type = NotificationTypes.LowStock,
                Title = "Alerte orpheline",
                Message = "Produit disparu",
                EntityId = 999_999,
                EntityType = NotificationEntityTypes.Product,
                IsRead = false,
                CreatedAt = DateTime.Now,
            });
            await context.SaveChangesAsync();
        }

        var result = await ReconcileAsync(dbPath);

        result.ResolvedCount.Should().Be(1);

        using var verify = CreateContext(dbPath);
        var orphan = await verify.Notifications.AsNoTracking().FirstAsync(n => n.EntityId == 999_999);
        orphan.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AlerteResolue_EstConserveeDansLaListe()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("history.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);
        await ReconcileAsync(dbPath);

        using var context = CreateContext(dbPath);
        var items = await new ListNotificationsUseCase(new NotificationRepository(context))
            .ExecuteAsync(new ListNotificationsQuery());

        items.Should().ContainSingle("la résolution est un marquage, jamais une suppression");
        items[0].IsResolved.Should().BeTrue();
        items[0].ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AlerteResolueNonLue_EstExclueDuCompteur()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("counter.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);
        var result = await ReconcileAsync(dbPath);

        result.UnreadCount.Should().Be(0, "le badge doit refléter des problèmes ACTIFS, pas un historique clos");

        using var context = CreateContext(dbPath);
        var counted = await new CountUnreadNotificationsUseCase(new NotificationRepository(context))
            .ExecuteAsync(new CountUnreadNotificationsQuery());
        counted.Should().Be(0);

        // La ligne existe pourtant toujours, et n'a jamais été lue : les deux axes sont bien distincts.
        var alerts = await LowStockAlertsAsync(dbPath, productId);
        alerts[0].IsRead.Should().BeFalse();
        alerts[0].ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AlerteActiveDejaLue_NEstPasRecreee()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("read-active.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);
        await MarkAllReadAsync(dbPath);

        var result = await ReconcileAsync(dbPath);

        result.CreatedCount.Should().Be(0);
        (await LowStockAlertsAsync(dbPath, productId)).Should().ContainSingle();
    }

    [Fact]
    public async Task DeuxiemeReconciliationApresResolution_EstIdempotente()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("resolve-twice.db", stock: 1, threshold: 5);
        await ReconcileAsync(dbPath);
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);

        var first = await ReconcileAsync(dbPath);
        var resolvedAt = (await LowStockAlertsAsync(dbPath, productId))[0].ResolvedAt;

        var second = await ReconcileAsync(dbPath);

        first.ResolvedCount.Should().Be(1);
        second.ResolvedCount.Should().Be(0, "la mise à jour est conditionnée à ResolvedAt IS NULL");
        (await LowStockAlertsAsync(dbPath, productId))[0].ResolvedAt.Should().Be(resolvedAt,
            "une seconde passe ne doit pas réécrire la date de résolution déjà posée");
    }

    // =========================================================================================================
    // Nouvel épisode
    // =========================================================================================================

    [Fact]
    public async Task NouvelleBaisseApresResolution_OuvreUneNouvelleAlerte()
    {
        // C'est LA capacité que l'option « anti-doublon à vie » aurait sacrifiée : une seconde pénurie doit alerter.
        var (dbPath, productId) = await SeedSingleProductAsync("episodes.db", stock: 1, threshold: 5);

        await ReconcileAsync(dbPath);                                             // épisode 1 : ouverture
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);
        await ReconcileAsync(dbPath);                                             // épisode 1 : résolution
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 2);
        var third = await ReconcileAsync(dbPath);                                 // épisode 2 : ouverture

        third.CreatedCount.Should().Be(1);

        var alerts = await LowStockAlertsAsync(dbPath, productId);
        alerts.Should().HaveCount(2);

        alerts[0].ResolvedAt.Should().NotBeNull("v1 reste un historique clos");
        alerts[1].ResolvedAt.Should().BeNull("v2 est l'épisode actif");
        alerts[1].IsRead.Should().BeFalse();
        alerts.Count(a => a.ResolvedAt == null).Should().Be(1, "exactement une alerte active par produit");
    }

    // =========================================================================================================
    // Changement de seuil — traité par le recalcul global, sans aucun code dédié
    // =========================================================================================================

    [Fact]
    public async Task SeuilAbaisseSousLeStock_ResoutLAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("threshold-down.db", stock: 4, threshold: 5);
        await ReconcileAsync(dbPath);

        await MutateProductAsync(dbPath, productId, p => p.StockAlertThreshold = 2);
        var result = await ReconcileAsync(dbPath);

        result.ResolvedCount.Should().Be(1);
        (await LowStockAlertsAsync(dbPath, productId))[0].ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SeuilReleveAuDessusDuStock_OuvreUneAlerte()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("threshold-up.db", stock: 10, threshold: 5);
        await ReconcileAsync(dbPath);
        (await LowStockAlertsAsync(dbPath, productId)).Should().BeEmpty();

        await MutateProductAsync(dbPath, productId, p => p.StockAlertThreshold = 20);
        var result = await ReconcileAsync(dbPath);

        result.CreatedCount.Should().Be(1);
        (await LowStockAlertsAsync(dbPath, productId)).Should().ContainSingle();
    }

    // =========================================================================================================
    // Compteur et lecture
    // =========================================================================================================

    [Fact]
    public async Task NotificationEvenementielleNonLue_ResteComptee()
    {
        // Les faits historiques ne sont jamais résolus : pour eux, le compteur garde exactement l'ancien sens.
        var (dbPath, productId) = await SeedSingleProductAsync("event-count.db", stock: 1, threshold: 5);

        using (var context = CreateContext(dbPath))
        {
            context.Notifications.Add(new Notification
            {
                Type = NotificationTypes.OrderStatusChanged,
                Title = "Commande",
                Message = "Transition",
                EntityId = 1,
                EntityType = NotificationEntityTypes.Order,
                IsRead = false,
                CreatedAt = DateTime.Now,
            });
            await context.SaveChangesAsync();
        }

        // Le stock remonte : l'alerte est résolue, mais le fait de commande reste compté.
        await ReconcileAsync(dbPath);
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);
        var result = await ReconcileAsync(dbPath);

        result.UnreadCount.Should().Be(1, "seule la notification de commande reste non lue ET non résolue");
    }

    [Fact]
    public async Task ListeComplete_ContientActivesEtResolues_TrieesParDateDecroissante()
    {
        var (dbPath, productId) = await SeedSingleProductAsync("list-all.db", stock: 1, threshold: 5);

        await ReconcileAsync(dbPath);
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 50);
        await ReconcileAsync(dbPath);                                      // résout v1
        await MutateProductAsync(dbPath, productId, p => p.StockQuantity = 1);
        await ReconcileAsync(dbPath);                                      // ouvre v2

        using var context = CreateContext(dbPath);
        var items = await new ListNotificationsUseCase(new NotificationRepository(context))
            .ExecuteAsync(new ListNotificationsQuery());

        items.Should().HaveCount(2);
        items.Count(i => i.IsResolved).Should().Be(1);
        items.Count(i => !i.IsResolved).Should().Be(1);
        items.Should().BeInDescendingOrder(i => i.CreatedAt, "le tri d'affichage existant est préservé");
    }
}
