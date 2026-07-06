using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.ListProductsForOrderPicker;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Products;

/// <summary>
/// P2D-6 — Query use case « Lister les produits pour le sélecteur de commande »
/// (<see cref="ListProductsForOrderPickerUseCase"/>). Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) :
/// la projection vers des <see cref="OrderProductPickerDto"/> plats (jamais l'entité EF <c>Product</c>), le portage
/// du prix de vente et de la catégorie (spécifiques au sélecteur de commande), le tri par nom, le cas vide et les
/// gardes.
/// </summary>
public sealed class ListProductsForOrderPickerUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListProductsForOrderPickerUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d6-order-products-" + Guid.NewGuid().ToString("N"));
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
            Category = ProductCategoryEnum.VERRE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 5m,
            SalePrice = 12m,
            StockQuantity = 3,
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task ListProducts_ReturnsProjectedDtos_SortedByName_WithSalePriceAndCategory()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);
        SeedTwoProducts(dbPath);

        IReadOnlyList<OrderProductPickerDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListProductsForOrderPickerUseCase(new ProductRepository(context));
            result = await useCase.ExecuteAsync(new ListProductsForOrderPickerQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<OrderProductPickerDto>();
        // Tri par nom (comme OrderFormViewModel.InitializeAsync).
        result[0].Name.Should().Be("Apple");
        result[1].Name.Should().Be("Zebra");
        // Portage fidèle des champs consommés par le sélecteur de commande (prix + catégorie).
        result[0].Reference.Should().Be("R1");
        result[0].SalePrice.Should().Be(12m);
        result[0].Category.Should().Be(ProductCategoryEnum.VERRE);
        result[1].SalePrice.Should().Be(25m);
        result[1].Category.Should().Be(ProductCategoryEnum.MONTURE);
        result[0].ProductId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListProducts_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListProductsForOrderPickerUseCase(new ProductRepository(context))
            .ExecuteAsync(new ListProductsForOrderPickerQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListProductsForOrderPickerUseCase(new ProductRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListProductsForOrderPickerUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
