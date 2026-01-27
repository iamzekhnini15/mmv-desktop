using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

public class ProductServiceTests
{
    private static OpticDbContext CreateContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<OpticDbContext>().UseSqlite(connection).Options;
        var context = new OpticDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task CreateProductAsync_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new ProductService(new UnitOfWork(context));
        var product = new Product
        {
            Name = "Monture Ray-Ban",
            Reference = "RB3025",
            SalePrice = 150m,
            PurchasePrice = 75m,
            StockQuantity = 10
        };
        
        var result = await service.CreateProductAsync(product);
        
        result.Should().NotBeNull();
        result!.Name.Should().Be("Monture Ray-Ban");
    }

    [Fact]
    public async Task CalculateMarginPercentage_ShouldCalculateCorrectly()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new ProductService(new UnitOfWork(context));
        var product = new Product { Name = "Test", Reference = "TST", SalePrice = 100m, PurchasePrice = 50m };
        
        var margin = service.CalculateMarginPercentage(product);
        
        margin.Should().Be(100m);
    }

    [Fact]
    public async Task GetProductAsync_ShouldReturnProduct()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new ProductService(new UnitOfWork(context));
        var product = new Product
        {
            Name = "Verre Progressif",
            Reference = "VP001",
            SalePrice = 200m,
            PurchasePrice = 100m,
            StockQuantity = 5
        };
        await service.CreateProductAsync(product);
        
        var retrieved = await service.GetProductAsync(product.ProductId);
        
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("Verre Progressif");
    }

    [Fact]
    public async Task DeleteProductAsync_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new ProductService(new UnitOfWork(context));
        var product = new Product
        {
            Name = "Accessoire",
            Reference = "AC001",
            SalePrice = 50m,
            PurchasePrice = 20m,
            StockQuantity = 20
        };
        await service.CreateProductAsync(product);
        
        await service.DeleteProductAsync(product.ProductId);
        
        var deleted = await service.GetProductAsync(product.ProductId);
        deleted.Should().BeNull();
    }
}
