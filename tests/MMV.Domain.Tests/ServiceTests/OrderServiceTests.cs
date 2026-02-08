using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Domain.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

public class OrderServiceTests
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
    public async Task CreateOrderAsync_WithValidItems_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Test", LastName = "Customer" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new OrderService(unitOfWork);
        var order = new Order
        {
            CustomerId = customer.CustomerId,
            OrderDate = DateTime.Now,
            Status = OrderStatus.New,
            OrderItems = new List<OrderItem> { new OrderItem { Quantity = 1, UnitPrice = 100m } }
        };
        
        var result = await service.CreateOrderAsync(order);
        
        result.Should().NotBeNull();
        result!.Status.Should().Be(OrderStatus.New);
    }

    [Fact]
    public async Task CreateOrderAsync_WithNoItems_ShouldThrowException()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Test", LastName = "Customer" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new OrderService(unitOfWork);
        var order = new Order { CustomerId = customer.CustomerId, OrderDate = DateTime.Now, Status = OrderStatus.New, OrderItems = new List<OrderItem>() };
        
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateOrderAsync(order));
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_WithValidTransition_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Test", LastName = "Customer" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new OrderService(unitOfWork);
        var order = new Order
        {
            CustomerId = customer.CustomerId,
            OrderDate = DateTime.Now,
            Status = OrderStatus.New,
            OrderItems = new List<OrderItem> { new OrderItem { Quantity = 1, UnitPrice = 100m } }
        };
        await service.CreateOrderAsync(order);
        
        await service.UpdateOrderStatusAsync(order.OrderId, OrderStatus.ToFabricate);
        
        var updated = await service.GetOrderAsync(order.OrderId);
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(OrderStatus.ToFabricate);
    }

    [Fact]
    public async Task GetOrderAsync_ShouldReturnOrder()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Test", LastName = "Customer" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new OrderService(unitOfWork);
        var order = new Order
        {
            CustomerId = customer.CustomerId,
            OrderDate = DateTime.Now,
            Status = OrderStatus.New,
            OrderItems = new List<OrderItem> { new OrderItem { Quantity = 2, UnitPrice = 50m } }
        };
        await service.CreateOrderAsync(order);
        
        var retrieved = await service.GetOrderAsync(order.OrderId);
        
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(OrderStatus.New);
    }
}
