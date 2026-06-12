using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Domain.Tests.Persistence;

/// <summary>
/// P2A-1D — Décrément de stock atomique conditionnel et intégrité des quantités (R-09).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que
/// <see cref="EfStockMutationService"/> et son intégration dans la frontière transactionnelle
/// (<see cref="EfTransactionRunner"/>) :
/// <list type="number">
///   <item>décrémentent un stock suffisant ;</item>
///   <item>autorisent un décrément <b>exactement égal</b> au stock disponible (→ 0) ;</item>
///   <item>refusent un décrément supérieur au stock disponible (erreur contrôlée) ;</item>
///   <item>ne persistent <b>jamais</b> de quantité négative ;</item>
///   <item>sous concurrence sur un stock limité, n'autorisent <b>qu'une seule</b> réussite ;</item>
///   <item>lèvent une erreur contrôlée (<see cref="InsufficientStockException"/>) en cas de stock
///         insuffisant ou de produit introuvable ;</item>
///   <item>refusent une quantité non positive (erreur d'appel) ;</item>
///   <item>annulent <b>toute</b> la vente (vente + stock) si le décrément échoue dans la transaction ;</item>
///   <item>valident vente + décrément ensemble en cas de succès.</item>
/// </list>
/// Chaque test utilise un fichier SQLite temporaire isolé ; l'état réellement validé est relu via un
/// contexte distinct.
/// </summary>
public sealed class EfStockMutationServiceTests : IDisposable
{
    private readonly string _workDirectory;

    public EfStockMutationServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2a1d-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Crée la base, insère un fournisseur + un produit (stock initial), renvoie son identifiant.</summary>
    private static long SeedProduct(string databasePath, int initialStock, string reference = "REF-001")
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();

        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = reference,
            Name = "Monture Test",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = initialStock,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Stock", LastName = "Client" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static int ReadStock(string databasePath, long productId)
    {
        using var verify = CreateContext(databasePath);
        return verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity;
    }

    private static Sale NewSale(long customerId, string saleNumber) => new()
    {
        CustomerId = customerId,
        SaleNumber = saleNumber,
        SaleDate = DateTime.UtcNow,
        TotalAmount = 100m,
        FinalAmount = 100m,
        PaymentMethod = PaymentMethod.Cash,
        Status = SaleStatus.Delivered,
    };

    // ------------------------------------------------------------------
    // (1) Décrément réussi
    // ------------------------------------------------------------------

    [Fact]
    public async Task DecrementStockAsync_SufficientStock_DecrementsAtomically()
    {
        var dbPath = PathFor("decrement-ok.db");
        var productId = SeedProduct(dbPath, initialStock: 10);

        using (var context = CreateContext(dbPath))
        {
            var service = new EfStockMutationService(context);
            await service.DecrementStockAsync(productId, 3);
        }

        ReadStock(dbPath, productId).Should().Be(7, "10 - 3 = 7");
    }

    // ------------------------------------------------------------------
    // (2) Décrément exactement égal au stock disponible → 0
    // ------------------------------------------------------------------

    [Fact]
    public async Task DecrementStockAsync_ExactlyAvailable_ReachesZero()
    {
        var dbPath = PathFor("decrement-exact.db");
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            var service = new EfStockMutationService(context);
            await service.DecrementStockAsync(productId, 5);
        }

        ReadStock(dbPath, productId).Should().Be(0, "un décrément égal au stock disponible est autorisé");
    }

    // ------------------------------------------------------------------
    // (3) Décrément supérieur au stock → erreur contrôlée, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task DecrementStockAsync_MoreThanAvailable_ThrowsControlled_AndPersistsNothing()
    {
        var dbPath = PathFor("decrement-over.db");
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            var service = new EfStockMutationService(context);

            Func<Task> act = () => service.DecrementStockAsync(productId, 6);

            var assertion = await act.Should().ThrowAsync<InsufficientStockException>();
            assertion.Which.ProductId.Should().Be(productId);
            assertion.Which.RequestedQuantity.Should().Be(6);
            assertion.Which.AvailableQuantity.Should().Be(5);
            assertion.Which.Message.Should().NotBeNullOrWhiteSpace();
        }

        ReadStock(dbPath, productId).Should().Be(5, "le décrément refusé ne modifie pas le stock");
    }

    // ------------------------------------------------------------------
    // (4) Aucune quantité négative persistée (cas type « sortie manuelle »)
    // ------------------------------------------------------------------

    [Fact]
    public async Task DecrementStockAsync_NeverPersistsNegativeStock()
    {
        var dbPath = PathFor("never-negative.db");
        var productId = SeedProduct(dbPath, initialStock: 2);

        using (var context = CreateContext(dbPath))
        {
            var service = new EfStockMutationService(context);

            // Sortie de 3 sur un stock de 2 : refusée par l'update conditionnel.
            Func<Task> act = () => service.DecrementStockAsync(productId, 3);
            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        ReadStock(dbPath, productId).Should().Be(2);
        ReadStock(dbPath, productId).Should().BeGreaterThanOrEqualTo(0, "aucune quantité négative ne doit jamais être persistée");
    }

    // ------------------------------------------------------------------
    // (5) Deux décréments concurrents sur un stock limité → une seule réussite
    // ------------------------------------------------------------------

    [Fact]
    public async Task DecrementStockAsync_TwoConcurrentOnSingleUnit_OnlyOneSucceeds()
    {
        var dbPath = PathFor("concurrent.db");
        var productId = SeedProduct(dbPath, initialStock: 1);

        // Deux opérations concurrentes, chacune sur sa propre connexion/contexte (Pooling=False),
        // décrémentent 1 sur un stock de 1.
        async Task<bool> TryDecrementAsync()
        {
            using var context = CreateContext(dbPath);
            var service = new EfStockMutationService(context);
            try
            {
                await service.DecrementStockAsync(productId, 1);
                return true; // réussi
            }
            catch (InsufficientStockException)
            {
                return false; // refusé (l'autre a gagné)
            }
        }

        var results = await Task.WhenAll(TryDecrementAsync(), TryDecrementAsync());

        results.Count(success => success).Should().Be(1, "une seule opération peut décrémenter la seule unité disponible");
        ReadStock(dbPath, productId).Should().Be(0, "le stock final est exactement 0, jamais négatif");
    }

    // ------------------------------------------------------------------
    // (6) Produit introuvable → erreur contrôlée
    // ------------------------------------------------------------------

    [Fact]
    public async Task DecrementStockAsync_ProductNotFound_ThrowsControlled()
    {
        var dbPath = PathFor("not-found.db");
        SeedProduct(dbPath, initialStock: 5); // crée la base/le schéma

        using var context = CreateContext(dbPath);
        var service = new EfStockMutationService(context);

        Func<Task> act = () => service.DecrementStockAsync(productId: 99999, quantity: 1);

        var assertion = await act.Should().ThrowAsync<InsufficientStockException>();
        assertion.Which.AvailableQuantity.Should().Be(0, "un produit inexistant a un disponible de 0");
    }

    // ------------------------------------------------------------------
    // (7) Quantité non positive → erreur d'appel, aucune écriture
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DecrementStockAsync_NonPositiveQuantity_ThrowsArgumentOutOfRange(int quantity)
    {
        var dbPath = PathFor($"non-positive-{quantity}.db");
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            var service = new EfStockMutationService(context);

            Func<Task> act = () => service.DecrementStockAsync(productId, quantity);
            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }

        ReadStock(dbPath, productId).Should().Be(5, "une quantité invalide ne modifie pas le stock");
    }

    // ------------------------------------------------------------------
    // (8) Vente transactionnelle : stock insuffisant → vente NON persistée (rollback vente + stock)
    // ------------------------------------------------------------------

    [Fact]
    public async Task SaleWithInsufficientStock_RollsBackSaleAndStock()
    {
        var dbPath = PathFor("sale-rollback.db");
        var productId = SeedProduct(dbPath, initialStock: 1);
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);
            var stock = new EfStockMutationService(context);

            Func<Task> act = () => runner.RunAsync(async ct =>
            {
                // 1) La vente est écrite (au sein de la transaction)…
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-ROLL"), ct);
                await unitOfWork.SaveChangesAsync(ct);

                // 2) …puis le décrément échoue (stock 1 < 5 demandés) → InsufficientStockException.
                await stock.DecrementStockAsync(productId, 5, ct);
            });

            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "la vente est annulée si le décrément de stock échoue");
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(1, "le stock reste inchangé après rollback");
    }

    // ------------------------------------------------------------------
    // (9) Vente transactionnelle : succès → vente + décrément validés ensemble
    // ------------------------------------------------------------------

    [Fact]
    public async Task SaleWithSufficientStock_CommitsSaleAndDecrement()
    {
        var dbPath = PathFor("sale-commit.db");
        var productId = SeedProduct(dbPath, initialStock: 5);
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);
            var stock = new EfStockMutationService(context);

            await runner.RunAsync(async ct =>
            {
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-OK"), ct);
                await unitOfWork.SaveChangesAsync(ct);
                await stock.DecrementStockAsync(productId, 2, ct);
            });
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(1, "la vente est validée");
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(3, "5 - 2 = 3, décrément validé avec la vente");
    }
}
