using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.DeleteProduct;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Products;

/// <summary>
/// P2C-GLOBAL — Use cases du module Produits (Create / Update / Delete), extraits iso-fonctionnellement de
/// <c>ProductFormViewModel.ExecuteSaveAsync</c> (avec ses détails de catégorie) et
/// <c>ProductsListViewModel.ConfirmAndDeleteProductAsync</c>. Vérifie sur un <b>vrai SQLite temporaire</b> la
/// persistance du produit et de son détail « accessoire », la mise à jour, la suppression et les gardes.
/// </summary>
public sealed class ProductUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    public ProductUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2cg-product-" + Guid.NewGuid().ToString("N"));
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

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static async Task<long> SeedSupplierAsync(string dbPath)
    {
        using var context = CreateContext(dbPath);
        var created = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Fournisseur" });
        await new UnitOfWork(context).SaveChangesAsync();
        return created.SupplierId;
    }

    [Fact]
    public async Task Create_PersistsProductWithAccessoryDetail()
    {
        var dbPath = PathFor("create.db");
        EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        CreateProductResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new CreateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
            result = await useCase.ExecuteAsync(new CreateProductCommand
            {
                Reference = "REF-1", Name = "Monture A", Category = ProductCategoryEnum.MONTURE,
                PurchasePrice = 10m, SalePrice = 25m, StockQuantity = 3, StockAlertThreshold = 5,
                SupplierId = supplierId, AccessoryColor = "Noir", AccessorySize = "52-18"
            });
        }

        result.ProductId.Should().BeGreaterThan(0);

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Include(p => p.AccessoryDetail).Single();
        product.Reference.Should().Be("REF-1");
        product.Category.Should().Be(ProductCategoryEnum.MONTURE);
        product.AccessoryDetail.Should().NotBeNull();
        product.AccessoryDetail!.Color.Should().Be("Noir");
        product.AccessoryDetail.Size.Should().Be("52-18");
    }

    [Fact]
    public async Task Update_ExistingProduct_AppliesChanges()
    {
        var dbPath = PathFor("update.db");
        EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new CreateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
            var created = await useCase.ExecuteAsync(new CreateProductCommand
            {
                Reference = "R", Name = "Old", Category = ProductCategoryEnum.MONTURE, SalePrice = 1m, SupplierId = supplierId
            });
            id = created.ProductId;
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new UpdateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
            var result = await useCase.ExecuteAsync(new UpdateProductCommand
            {
                ProductId = id, Reference = "R2", Name = "New", Category = ProductCategoryEnum.MONTURE,
                SalePrice = 99m, StockQuantity = 7, SupplierId = supplierId
            });
            result.ProductFound.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.Name.Should().Be("New");
        product.Reference.Should().Be("R2");
        product.SalePrice.Should().Be(99m);
        product.StockQuantity.Should().Be(7);
    }

    [Fact]
    public async Task Update_MissingProduct_ReturnsNotFound()
    {
        var dbPath = PathFor("update-missing.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var useCase = new UpdateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
        var result = await useCase.ExecuteAsync(new UpdateProductCommand { ProductId = 999, Reference = "x", Name = "x", Category = ProductCategoryEnum.MONTURE });
        result.ProductFound.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_ExistingProduct_RemovesIt()
    {
        var dbPath = PathFor("delete.db");
        EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        long id;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new CreateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
            var created = await useCase.ExecuteAsync(new CreateProductCommand { Reference = "D", Name = "Del", Category = ProductCategoryEnum.MONTURE, SalePrice = 1m, SupplierId = supplierId });
            id = created.ProductId;
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new DeleteProductUseCase(new ProductRepository(context), new UnitOfWork(context));
            var result = await useCase.ExecuteAsync(new DeleteProductCommand { ProductId = id });
            result.ProductFound.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_MissingProduct_ReturnsNotFound()
    {
        var dbPath = PathFor("delete-missing.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var useCase = new DeleteProductUseCase(new ProductRepository(context), new UnitOfWork(context));
        var result = await useCase.ExecuteAsync(new DeleteProductCommand { ProductId = 555 });
        result.ProductFound.Should().BeFalse();
    }

    [Fact]
    public async Task NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var create = new CreateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
        var update = new UpdateProductUseCase(new ProductRepository(context), new UnitOfWork(context), new EfTransactionRunner(context));
        var delete = new DeleteProductUseCase(new ProductRepository(context), new UnitOfWork(context));

        await ((Func<Task>)(() => create.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => update.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => delete.ExecuteAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_RejectNullDependencies()
    {
        ((Action)(() => _ = new CreateProductUseCase(null!, null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new UpdateProductUseCase(null!, null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new DeleteProductUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
    }
}
