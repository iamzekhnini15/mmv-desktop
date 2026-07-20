using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P3-8 — Revue ciblée §5 : preuve que l'insertion atomique SQL brute (<c>TryCreateActiveLowStockAsync</c>) et la
/// résolution ensembliste (<c>ResolveActiveLowStockAsync</c>, <c>ExecuteUpdateAsync</c>) appartiennent bien à la
/// <b>même</b> transaction EF ouverte par <see cref="EfTransactionRunner"/>, sur <b>vraie base SQLite jetable</b>.
///
/// <para>
/// Le rapport d'implémentation affirmait l'atomicité de la réconciliation sans test dédié prouvant que le SQL brut
/// (<c>ExecuteSqlRawAsync</c>, hors du suivi EF) et l'<c>ExecuteUpdateAsync</c> partagent la transaction ambiante et
/// s'annulent ensemble en cas d'échec. C'est prouvé ici en pilotant directement <see cref="EfTransactionRunner"/> —
/// sans passer par <c>GenerateLowStockNotificationsUseCase</c> — pour pouvoir provoquer une exception déterministe
/// après les deux écritures et avant le commit.
/// </para>
/// </summary>
public sealed class LowStockTransactionalEnrollmentTests : IDisposable
{
    private readonly string _workDirectory;

    public LowStockTransactionalEnrollmentTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p38-txn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    /// <summary>Crée la base, deux produits sous seuil, et une alerte active pour le premier.</summary>
    private async Task<(string DbPath, long ResolvingProductId, long CreatingProductId)> SeedAsync(string fileName)
    {
        var dbPath = PathFor(fileName);
        using (var schema = CreateContext(dbPath))
        {
            schema.Database.EnsureCreated();
        }

        long resolvingProductId;
        long creatingProductId;
        using (var context = CreateContext(dbPath))
        {
            var supplier = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "S" });
            await new UnitOfWork(context).SaveChangesAsync();

            var products = new ProductRepository(context);
            var resolving = await products.CreateAsync(new Product
            {
                Reference = "REF-A", Name = "A", Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId, StockQuantity = 1, StockAlertThreshold = 5,
            });
            var creating = await products.CreateAsync(new Product
            {
                Reference = "REF-B", Name = "B", Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId, StockQuantity = 1, StockAlertThreshold = 5,
            });
            await new UnitOfWork(context).SaveChangesAsync();
            resolvingProductId = resolving.ProductId;
            creatingProductId = creating.ProductId;

            context.Notifications.Add(new Notification
            {
                Type = NotificationTypes.LowStock,
                Title = "Stock bas : A",
                Message = "m",
                EntityId = resolvingProductId,
                EntityType = NotificationEntityTypes.Product,
                IsRead = false,
                CreatedAt = DateTime.Now,
            });
            await context.SaveChangesAsync();
        }

        return (dbPath, resolvingProductId, creatingProductId);
    }

    private static Notification LowStockFor(long productId) => new()
    {
        Type = NotificationTypes.LowStock,
        Title = "Stock bas : B",
        Message = "m",
        EntityId = productId,
        EntityType = NotificationEntityTypes.Product,
        IsRead = false,
        CreatedAt = DateTime.Now,
    };

    [Fact]
    public async Task EchecAvantCommit_AnnuleEnsembleLaResolutionSqlBruteEtLaCreationSqlBrute()
    {
        var (dbPath, resolvingProductId, creatingProductId) = await SeedAsync("rollback.db");

        using (var context = CreateContext(dbPath))
        {
            var repository = new NotificationRepository(context);
            var runner = new EfTransactionRunner(context);

            var act = async () => await runner.RunAsync(async token =>
            {
                // (1) Résolution ensembliste — ExecuteUpdateAsync, suivi EF.
                var resolved = await repository.ResolveActiveLowStockAsync(new[] { resolvingProductId }, DateTime.Now, token);
                resolved.Should().Be(1, "l'alerte du produit A doit être résolue par cette étape, avant l'échec");

                // (2) Création atomique — SQL brut, ExecuteSqlRawAsync, HORS suivi EF.
                var created = await repository.TryCreateActiveLowStockAsync(LowStockFor(creatingProductId), token);
                created.Should().BeTrue("la nouvelle alerte du produit B doit être ouverte par cette étape, avant l'échec");

                // (3) Échec délibéré AVANT le commit : le runner doit annuler (1) ET (2), qui n'ont donc jamais dû
                //     appartenir qu'à une seule et même transaction.
                throw new InvalidOperationException("Échec volontaire avant commit — preuve de rollback.");

#pragma warning disable CS0162 // code inaccessible intentionnel : jamais atteint, prouve le contrat du type de retour
                return true;
#pragma warning restore CS0162
            });

            await act.Should().ThrowAsync<InvalidOperationException>("l'exception d'origine doit être propagée inchangée, jamais absorbée");
        }

        // (4) Relecture depuis un CONTEXTE NEUF : aucun état partiellement réconcilié ne doit être visible.
        using var verify = CreateContext(dbPath);
        var resolvingAlert = await verify.Notifications.AsNoTracking().SingleAsync(n => n.EntityId == resolvingProductId);
        resolvingAlert.ResolvedAt.Should().BeNull(
            "la résolution du produit A doit avoir été annulée par le rollback : elle appartenait à la même transaction que la création");

        var creatingAlerts = await verify.Notifications.AsNoTracking().Where(n => n.EntityId == creatingProductId).ToListAsync();
        creatingAlerts.Should().BeEmpty(
            "la création SQL brute du produit B doit avoir été annulée par le rollback, alors qu'elle échappe au ChangeTracker EF");
    }

    [Fact]
    public async Task CasNominal_ResolutionEtCreationSontToutesDeuxPersisteesApresCommit()
    {
        var (dbPath, resolvingProductId, creatingProductId) = await SeedAsync("commit.db");

        using (var context = CreateContext(dbPath))
        {
            var repository = new NotificationRepository(context);
            var runner = new EfTransactionRunner(context);

            await runner.RunAsync(async token =>
            {
                await repository.ResolveActiveLowStockAsync(new[] { resolvingProductId }, DateTime.Now, token);
                await repository.TryCreateActiveLowStockAsync(LowStockFor(creatingProductId), token);
                return true;
            });
        }

        using var verify = CreateContext(dbPath);
        var resolvingAlert = await verify.Notifications.AsNoTracking().SingleAsync(n => n.EntityId == resolvingProductId);
        resolvingAlert.ResolvedAt.Should().NotBeNull("sans échec, la résolution doit être commitée");

        var creatingAlert = await verify.Notifications.AsNoTracking().SingleAsync(n => n.EntityId == creatingProductId);
        creatingAlert.ResolvedAt.Should().BeNull("sans échec, la nouvelle alerte doit être commitée et active");
    }
}
