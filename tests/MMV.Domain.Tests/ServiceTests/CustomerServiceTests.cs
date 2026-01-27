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

public class CustomerServiceTests
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
    public async Task CreateCustomerAsync_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new CustomerService(new UnitOfWork(context));
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        
        var result = await service.CreateCustomerAsync(customer);
        
        result.Should().NotBeNull();
        result!.FirstName.Should().Be("Jean");
    }

    [Fact]
    public async Task GetCustomerAsync_ShouldReturnCustomer()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new CustomerService(new UnitOfWork(context));
        var customer = new Customer { FirstName = "Marie", LastName = "Martin" };
        await service.CreateCustomerAsync(customer);
        
        var retrieved = await service.GetCustomerAsync(customer.CustomerId);
        
        retrieved.Should().NotBeNull();
        retrieved!.FirstName.Should().Be("Marie");
    }

    [Fact]
    public async Task DeleteCustomerAsync_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var service = new CustomerService(new UnitOfWork(context));
        var customer = new Customer { FirstName = "Anne", LastName = "Leclerc" };
        await service.CreateCustomerAsync(customer);
        
        await service.DeleteCustomerAsync(customer.CustomerId);
        
        var deleted = await service.GetCustomerAsync(customer.CustomerId);
        deleted.Should().BeNull();
    }
}
