using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

public class SaleServiceTests
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
    public async Task CreateSaleAsync_WithValidSale_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Test", LastName = "Customer" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new SaleService(unitOfWork);
        var sale = new Sale
        {
            CustomerId = customer.CustomerId,
            SaleDate = DateTime.Now,
            SaleNumber = "VTE-2026-0001",
            TotalAmount = 100m,
            DiscountAmount = 10m,
            FinalAmount = 90m,
            PaymentMethod = Domain.Enums.PaymentMethod.Card,
            SaleItems = new List<SaleItem> { new SaleItem { Quantity = 1, UnitPrice = 100m, TotalPrice = 100m } }
        };
        
        var result = await service.CreateSaleAsync(sale);
        
        result.Should().NotBeNull();
        result!.TotalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task CreateSaleAsync_WithNoItems_ShouldThrowException()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Test", LastName = "Customer" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new SaleService(unitOfWork);
        var sale = new Sale { CustomerId = customer.CustomerId, SaleDate = DateTime.Now, SaleNumber = "VTE-2026-0002", TotalAmount = 0m, PaymentMethod = Domain.Enums.PaymentMethod.Card, SaleItems = new List<SaleItem>() };
        
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateSaleAsync(sale));
    }

    [Fact]
    public async Task CalculateSaleAsync_ShouldCalculateFinalAmount()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new SaleService(new UnitOfWork(context));
        var sale = new Sale
        {
            SaleDate = DateTime.Now,
            SaleNumber = "VTE-2026-0003",
            TotalAmount = 100m,
            DiscountAmount = 10m,
            PaymentMethod = Domain.Enums.PaymentMethod.Card,
            SaleItems = new List<SaleItem> { new SaleItem { Quantity = 1, UnitPrice = 100m, TotalPrice = 100m } }
        };
        
        var result = await service.CalculateSaleAsync(sale);
        
        result.FinalAmount.Should().Be(90m);
    }

    [Fact]
    public async Task GetSaleAsync_ShouldReturnSale()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Marie", LastName = "Martin" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new SaleService(unitOfWork);
        var sale = new Sale
        {
            CustomerId = customer.CustomerId,
            SaleDate = DateTime.Now,
            SaleNumber = "VTE-2026-0004",
            TotalAmount = 200m,
            DiscountAmount = 20m,
            FinalAmount = 180m,
            PaymentMethod = Domain.Enums.PaymentMethod.Card,
            SaleItems = new List<SaleItem> { new SaleItem { Quantity = 2, UnitPrice = 100m, TotalPrice = 200m } }
        };
        await service.CreateSaleAsync(sale);
        
        var retrieved = await service.GetSaleAsync(sale.SaleId);
        
        retrieved.Should().NotBeNull();
        retrieved!.TotalAmount.Should().Be(200m);
    }

    [Fact]
    public async Task GetCustomerSalesAsync_ShouldReturnAllCustomerSales()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Pierre", LastName = "Bernard" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new SaleService(unitOfWork);
        var sale1 = new Sale
        {
            CustomerId = customer.CustomerId,
            SaleDate = DateTime.Now,
            SaleNumber = "VTE-2026-0005",
            TotalAmount = 100m,
            PaymentMethod = Domain.Enums.PaymentMethod.Card,
            SaleItems = new List<SaleItem> { new SaleItem { Quantity = 1, UnitPrice = 100m, TotalPrice = 100m } }
        };
        var sale2 = new Sale
        {
            CustomerId = customer.CustomerId,
            SaleDate = DateTime.Now.AddDays(-1),
            SaleNumber = "VTE-2026-0006",
            TotalAmount = 150m,
            PaymentMethod = Domain.Enums.PaymentMethod.Cash,
            SaleItems = new List<SaleItem> { new SaleItem { Quantity = 1, UnitPrice = 150m, TotalPrice = 150m } }
        };
        await service.CreateSaleAsync(sale1);
        await service.CreateSaleAsync(sale2);
        
        var sales = await service.GetCustomerSalesAsync(customer.CustomerId);
        
        sales.Should().HaveCount(2);
    }
}

