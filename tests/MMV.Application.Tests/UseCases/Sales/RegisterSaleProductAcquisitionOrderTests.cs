using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P3-7 (revue avant commit) — Ordre déterministe des prises de produits actifs par
/// <see cref="RegisterSaleUseCase"/>.
/// </summary>
/// <remarks>
/// Deux ventes contenant les mêmes produits dans des ordres de panier <b>opposés</b> doivent acquérir leurs
/// verrous dans la <b>même</b> séquence (tri croissant par <see cref="Product.ProductId"/>), afin de réduire le
/// risque d'interblocage sur un futur provider serveur multi-connexions (l'ordre ne doit dépendre ni du panier, ni
/// d'un <c>HashSet</c>/dictionnaire, ni de l'ordre de restitution EF). Prouvé par un décorateur d'observation
/// autour d'<see cref="IProductRepository"/>, délégant tout à un <see cref="ProductRepository"/> réel.
/// </remarks>
public sealed class RegisterSaleProductAcquisitionOrderTests : IDisposable
{
    private readonly string _workDirectory;

    public RegisterSaleProductAcquisitionOrderTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-7-order-" + Guid.NewGuid().ToString("N"));
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

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Use", LastName = "Case" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static long SeedProduct(string databasePath, string reference)
    {
        using var context = CreateContext(databasePath);
        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = reference,
            Name = "Produit Test",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = 10,
            IsActive = true,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private static RegisterSaleLineCommand Frame(long productId, int quantity, decimal unitPrice)
        => new() { ProductId = productId, ItemType = OrderItemType.Frame, Quantity = quantity, UnitPrice = unitPrice };

    /// <summary>
    /// Décorateur d'observation : délègue tout à un <see cref="ProductRepository"/> réel (même <c>DbContext</c>),
    /// en enregistrant l'ordre exact des identifiants soumis à <see cref="TryAcquireActiveAsync"/> — la seule
    /// méthode qui matérialise la séquence de prise.
    /// </summary>
    private sealed class RecordingProductRepository : IProductRepository
    {
        private readonly ProductRepository _inner;
        public List<long> AcquisitionOrder { get; } = new();

        public RecordingProductRepository(OpticDbContext context) => _inner = new ProductRepository(context);

        public Task<bool> TryAcquireActiveAsync(long productId, CancellationToken cancellationToken = default)
        {
            AcquisitionOrder.Add(productId);
            return _inner.TryAcquireActiveAsync(productId, cancellationToken);
        }

        public Task<Product?> GetByIdFreshAsync(long productId, CancellationToken cancellationToken = default)
            => _inner.GetByIdFreshAsync(productId, cancellationToken);

        public Task<IReadOnlyList<Product>> GetActiveLowStockAsync(CancellationToken cancellationToken = default)
            => _inner.GetActiveLowStockAsync(cancellationToken);

        public Task<Product?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default)
            => _inner.GetByReferenceAsync(reference, cancellationToken);

        public Task<bool> ExistsByNormalizedReferenceAsync(string normalizedReference, long excludingProductId, CancellationToken cancellationToken = default)
            => _inner.ExistsByNormalizedReferenceAsync(normalizedReference, excludingProductId, cancellationToken);

        public Task<bool> IsReferencedByHistoryAsync(long productId, CancellationToken cancellationToken = default)
            => _inner.IsReferencedByHistoryAsync(productId, cancellationToken);

        public Task<IList<Product>> GetByCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
            => _inner.GetByCategoryAsync(categoryId, cancellationToken);

        public Task<IList<Product>> GetBySupplierAsync(long supplierId, CancellationToken cancellationToken = default)
            => _inner.GetBySupplierAsync(supplierId, cancellationToken);

        public Task<IList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default)
            => _inner.GetLowStockProductsAsync(cancellationToken);

        public Task<IList<Product>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default)
            => _inner.SearchByNameAsync(searchTerm, cancellationToken);

        public Task<IList<Product>> GetActiveProductsAsync(CancellationToken cancellationToken = default)
            => _inner.GetActiveProductsAsync(cancellationToken);

        public Task UpdateCatalogAsync(Product product, CancellationToken cancellationToken = default)
            => _inner.UpdateCatalogAsync(product, cancellationToken);

        public Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default)
            => _inner.GetByIdWithDetailsAsync(id, cancellationToken);

        public Task<Product?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => _inner.GetByIdAsync(id, cancellationToken);

        public Task<IList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
            => _inner.GetAllAsync(cancellationToken);

        public Task<Product> CreateAsync(Product entity, CancellationToken cancellationToken = default)
            => _inner.CreateAsync(entity, cancellationToken);

        public Task<Product> UpdateAsync(Product entity, CancellationToken cancellationToken = default)
            => _inner.UpdateAsync(entity, cancellationToken);

        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(id, cancellationToken);

        public Task DeleteAsync(Product entity, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(entity, cancellationToken);

        public Task<bool> ExistsAsync(long id, CancellationToken cancellationToken = default)
            => _inner.ExistsAsync(id, cancellationToken);
    }

    private static RegisterSaleUseCase CreateUseCase(OpticDbContext context, RecordingProductRepository productRepository)
        => new(
            new SaleRepository(context),
            new OrderRepository(context),
            productRepository,
            new CustomerRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            new EfNumberSequenceService(context),
            SystemClock.Instance);

    [Fact]
    public async Task OrdrePanierOppose_ProduitLaMemeSequenceDePrise_TrieeParProductId()
    {
        var dbPath = PathFor("order.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        var lowId = SeedProduct(dbPath, "MON-LOW");
        var highId = SeedProduct(dbPath, "MON-HIGH");
        lowId.Should().BeLessThan(highId, "l'auto-incrément garantit low < high");

        // Commande 1 : panier « high puis low ».
        List<long> order1;
        using (var context = CreateContext(dbPath))
        {
            var repo = new RecordingProductRepository(context);
            await CreateUseCase(context, repo).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(highId, 1, 10m), Frame(lowId, 1, 10m) }
            });
            order1 = repo.AcquisitionOrder;
        }

        // Commande 2 : panier « low puis high » (ordre opposé).
        List<long> order2;
        using (var context = CreateContext(dbPath))
        {
            var repo = new RecordingProductRepository(context);
            await CreateUseCase(context, repo).ExecuteAsync(new RegisterSaleCommand
            {
                CustomerId = customerId,
                IsCounterSale = true,
                PaymentMethod = PaymentMethod.Cash,
                Lines = new[] { Frame(lowId, 1, 10m), Frame(highId, 1, 10m) }
            });
            order2 = repo.AcquisitionOrder;
        }

        order1.Should().Equal(new[] { lowId, highId }, "l'ordre de prise est trié par ProductId, jamais par le panier");
        order2.Should().Equal(new[] { lowId, highId }, "le même ordre trié est observé quel que soit l'ordre du panier");
    }

    [Fact]
    public async Task ProduitPresentSurPlusieursLignes_NEstPrisQuUneSeuleFois()
    {
        var dbPath = PathFor("dedup.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);
        var productId = SeedProduct(dbPath, "MON-DUP");

        using var context = CreateContext(dbPath);
        var repo = new RecordingProductRepository(context);
        await CreateUseCase(context, repo).ExecuteAsync(new RegisterSaleCommand
        {
            CustomerId = customerId,
            IsCounterSale = true,
            PaymentMethod = PaymentMethod.Cash,
            Lines = new[] { Frame(productId, 1, 10m), Frame(productId, 2, 10m) }
        });

        repo.AcquisitionOrder.Should().Equal(new[] { productId }, "un produit présent sur plusieurs lignes n'est pris qu'une seule fois");
    }
}
