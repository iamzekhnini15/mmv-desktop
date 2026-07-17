using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.ListProducts;
using MMV.Application.UseCases.Products.ListProductsForOrderPicker;
using MMV.Application.UseCases.Products.ListProductsForPicker;
using MMV.Application.UseCases.Sales.GetSaleFormReferenceData;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Products;

/// <summary>
/// P3-4B — Un produit désactivé est exclu des sélecteurs de <b>nouvelle opération</b> (vente, commande, stock)
/// mais reste présent dans la <b>liste principale d'administration</b> (pour consultation / réactivation).
/// </summary>
public sealed class ProductSelectorActiveFilterTests : IDisposable
{
    private readonly string _workDirectory;

    public ProductSelectorActiveFilterTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p34b-selectors-" + Guid.NewGuid().ToString("N"));
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

    private long _customerId;

    private void SeedActiveAndInactive(string dbPath)
    {
        using var context = CreateContext(dbPath);
        context.Database.EnsureCreated();
        var supplier = new Supplier { Name = "F" };
        context.Suppliers.Add(supplier);
        var customer = new Customer { FirstName = "A", LastName = "B" };
        context.Customers.Add(customer);
        context.SaveChanges();
        _customerId = customer.CustomerId;

        context.Products.Add(new Product { Reference = "ACT", Name = "Actif", Category = ProductCategoryEnum.MONTURE, SupplierId = supplier.SupplierId, SalePrice = 10m, PurchasePrice = 5m, IsActive = true });
        context.Products.Add(new Product { Reference = "INA", Name = "Inactif", Category = ProductCategoryEnum.MONTURE, SupplierId = supplier.SupplierId, SalePrice = 10m, PurchasePrice = 5m, IsActive = false });
        context.SaveChanges();
    }

    [Fact]
    public async Task OrderPicker_ExcludesInactive()
    {
        var dbPath = PathFor("order.db"); SeedActiveAndInactive(dbPath);
        using var ctx = CreateContext(dbPath);
        var result = await new ListProductsForOrderPickerUseCase(new ProductRepository(ctx)).ExecuteAsync(new ListProductsForOrderPickerQuery());
        result.Should().ContainSingle().Which.Reference.Should().Be("ACT");
    }

    [Fact]
    public async Task StockPicker_ExcludesInactive()
    {
        var dbPath = PathFor("stock.db"); SeedActiveAndInactive(dbPath);
        using var ctx = CreateContext(dbPath);
        var result = await new ListProductsForPickerUseCase(new ProductRepository(ctx)).ExecuteAsync(new ListProductsForPickerQuery());
        result.Should().ContainSingle().Which.Reference.Should().Be("ACT");
    }

    [Fact]
    public async Task SaleSelector_ExcludesInactive()
    {
        var dbPath = PathFor("sale.db"); SeedActiveAndInactive(dbPath);
        using var ctx = CreateContext(dbPath);
        var result = await new GetSaleFormReferenceDataUseCase(new ProductRepository(ctx), new PrescriptionRepository(ctx))
            .ExecuteAsync(new GetSaleFormReferenceDataQuery { CustomerId = _customerId });
        result.Products.Should().ContainSingle().Which.Reference.Should().Be("ACT");
    }

    [Fact]
    public async Task MainAdminList_IncludesInactive()
    {
        var dbPath = PathFor("list.db"); SeedActiveAndInactive(dbPath);
        using var ctx = CreateContext(dbPath);
        var result = await new ListProductsUseCase(new ProductRepository(ctx)).ExecuteAsync(new ListProductsQuery());
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Reference == "INA" && !p.IsActive);
    }
}
