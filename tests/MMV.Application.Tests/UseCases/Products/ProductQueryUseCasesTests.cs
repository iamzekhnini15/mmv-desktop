using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.GetInventoryOverview;
using MMV.Application.UseCases.Products.ListProductsForPicker;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Products;

/// <summary>
/// P2D-4 — Query use cases de lecture du module Produits utilisés par le module Stock/Inventaire :
/// <see cref="ListProductsForPickerUseCase"/> (sélecteurs produit) et <see cref="GetInventoryOverviewUseCase"/>
/// (inventaire). Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers DTO plats
/// (jamais d'entité EF), le tri par nom, le rattachement du nom de fournisseur, le cas vide et les gardes.
/// </summary>
public sealed class ProductQueryUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    public ProductQueryUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d4-prodq-" + Guid.NewGuid().ToString("N"));
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

    private static void SeedTwoProducts(string databasePath)
    {
        using var context = CreateContext(databasePath);

        var supplier = new Supplier { Name = "Essilor" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        context.Products.Add(new Product
        {
            Reference = "R2",
            Name = "Zebra",
            Description = "Monture zébrée",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 25m,
            StockQuantity = 7,
        });
        context.Products.Add(new Product
        {
            Reference = "R1",
            Name = "Apple",
            Description = "Verre pomme",
            Category = ProductCategoryEnum.VERRE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 5m,
            SalePrice = 12m,
            StockQuantity = 3,
        });
        context.SaveChanges();
    }

    // ---------------- ListProductsForPicker ----------------

    [Fact]
    public async Task ListProductsForPicker_ReturnsDtos_SortedByName_WithSupplierName()
    {
        var dbPath = PathFor("picker.db");
        EnsureSchema(dbPath);
        SeedTwoProducts(dbPath);

        IReadOnlyList<ProductPickerItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListProductsForPickerUseCase(new ProductRepository(context));
            result = await useCase.ExecuteAsync(new ListProductsForPickerQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<ProductPickerItemDto>();
        result.Select(p => p.Name).Should().ContainInOrder("Apple", "Zebra");
        var apple = result.Single(p => p.Name == "Apple");
        apple.Reference.Should().Be("R1");
        apple.SupplierName.Should().Be("Essilor");
        apple.Description.Should().Be("Verre pomme");
    }

    [Fact]
    public async Task ListProductsForPicker_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("picker-empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListProductsForPickerUseCase(new ProductRepository(context))
            .ExecuteAsync(new ListProductsForPickerQuery());
        result.Should().BeEmpty();
    }

    // ---------------- GetInventoryOverview ----------------

    [Fact]
    public async Task GetInventoryOverview_ReturnsDtos_SortedByName_WithStockAndCategory()
    {
        var dbPath = PathFor("inventory.db");
        EnsureSchema(dbPath);
        SeedTwoProducts(dbPath);

        IReadOnlyList<InventoryProductItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new GetInventoryOverviewUseCase(new ProductRepository(context));
            result = await useCase.ExecuteAsync(new GetInventoryOverviewQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<InventoryProductItemDto>();
        result.Select(p => p.Name).Should().ContainInOrder("Apple", "Zebra");
        var apple = result.Single(p => p.Name == "Apple");
        apple.Reference.Should().Be("R1");
        apple.StockQuantity.Should().Be(3);
        apple.Category.Should().Be(ProductCategoryEnum.VERRE);
    }

    [Fact]
    public async Task GetInventoryOverview_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("inventory-empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new GetInventoryOverviewUseCase(new ProductRepository(context))
            .ExecuteAsync(new GetInventoryOverviewQuery());
        result.Should().BeEmpty();
    }

    // ---------------- Gardes ----------------

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListProductsForPickerUseCase(new ProductRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => new GetInventoryOverviewUseCase(new ProductRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_RejectNullRepository()
    {
        ((Action)(() => _ = new ListProductsForPickerUseCase(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new GetInventoryOverviewUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
