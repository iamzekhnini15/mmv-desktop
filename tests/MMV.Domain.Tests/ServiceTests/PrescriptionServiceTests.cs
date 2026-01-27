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

public class PrescriptionServiceTests
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
    public async Task CreatePrescriptionAsync_ShouldSucceed()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new PrescriptionService(unitOfWork);
        var prescription = new Prescription
        {
            CustomerId = customer.CustomerId,
            IssueDate = DateTime.Today,
            OdSphere = 2.5,
            OdCylinder = -1.0,
            OdAxis = 90
        };
        
        var result = await service.CreatePrescriptionAsync(prescription);
        
        result.Should().NotBeNull();
        result!.OdSphere.Should().Be(2.5);
    }

    [Fact]
    public async Task CreatePrescriptionAsync_WithFutureDate_ShouldThrowException()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Marie", LastName = "Martin" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new PrescriptionService(unitOfWork);
        var prescription = new Prescription
        {
            CustomerId = customer.CustomerId,
            IssueDate = DateTime.Today.AddDays(2),
            OdSphere = 2.5
        };
        
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreatePrescriptionAsync(prescription));
    }

    [Fact]
    public async Task GetPrescriptionAsync_ShouldReturnPrescription()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Pierre", LastName = "Bernard" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new PrescriptionService(unitOfWork);
        var prescription = new Prescription
        {
            CustomerId = customer.CustomerId,
            IssueDate = DateTime.Today,
            OdSphere = 1.5
        };
        await service.CreatePrescriptionAsync(prescription);
        
        var retrieved = await service.GetPrescriptionAsync(prescription.PrescriptionId);
        
        retrieved.Should().NotBeNull();
        retrieved!.OdSphere.Should().Be(1.5);
    }

    [Fact]
    public async Task GetCustomerPrescriptionsAsync_ShouldReturnAllPrescriptions()
    {
        using var context = CreateContext(out var connection);
        await using var _ = connection;
        var unitOfWork = new UnitOfWork(context);
        var customer = new Customer { FirstName = "Anne", LastName = "Leclerc" };
        await unitOfWork.Customers.CreateAsync(customer);
        await unitOfWork.SaveChangesAsync();
        
        var service = new PrescriptionService(unitOfWork);
        var prescription1 = new Prescription { CustomerId = customer.CustomerId, IssueDate = DateTime.Today.AddMonths(-1) };
        var prescription2 = new Prescription { CustomerId = customer.CustomerId, IssueDate = DateTime.Today };
        await service.CreatePrescriptionAsync(prescription1);
        await service.CreatePrescriptionAsync(prescription2);
        
        var prescriptions = await service.GetCustomerPrescriptionsAsync(customer.CustomerId);
        
        prescriptions.Should().HaveCount(2);
    }
}
