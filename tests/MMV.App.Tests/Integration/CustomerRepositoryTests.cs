using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Xunit.Abstractions;

namespace MMV.App.Tests.Integration;

/// <summary>
/// Tests d'intégration pour CustomerRepository.
/// </summary>
public class CustomerRepositoryTests : IDisposable
{
    private readonly OpticDbContext _context;
    private readonly CustomerRepository _repository;
    private readonly UnitOfWork _unitOfWork;
    private readonly ITestOutputHelper _output;
    private readonly string _dbPath;

    public CustomerRepositoryTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Créer une base de données SQLite en mémoire pour les tests
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_mmv_{Guid.NewGuid()}.db");
        _output.WriteLine($"[TEST] Creating test database at: {_dbPath}");
        
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _output.WriteLine("[TEST] Database created successfully");
        
        _unitOfWork = new UnitOfWork(_context);
        _repository = new CustomerRepository(_context);
        
        // Initialiser avec des données de test
        SeedTestData();
    }

    private void SeedTestData()
    {
        _output.WriteLine("[TEST] Seeding test data...");
        
        var customers = new[]
        {
            new Customer
            {
                FirstName = "Ali",
                LastName = "Zekhnini",
                Email = "ali@test.com",
                Phone = "0612345678",
                BirthDate = new DateTime(1990, 1, 1),
                Address = "123 Rue Test",
                City = "Paris",
                PostalCode = "75001",
                Country = "France",
                Notes = "Test customer 1",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                FirstName = "Marie",
                LastName = "Dupont",
                Email = "marie@test.com",
                Phone = "0698765432",
                BirthDate = new DateTime(1985, 5, 15),
                Address = "456 Avenue Test",
                City = "Lyon",
                PostalCode = "69001",
                Country = "France",
                Notes = "Test customer 2",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new Customer
            {
                FirstName = "Jean",
                LastName = "Martin",
                Email = "jean@test.com",
                Phone = "0687654321",
                BirthDate = new DateTime(1975, 12, 25),
                Address = "789 Boulevard Test",
                City = "Marseille",
                PostalCode = "13001",
                Country = "France",
                Notes = "Test customer 3",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        _context.Customers.AddRange(customers);
        _context.SaveChanges();
        
        _output.WriteLine($"[TEST] Seeded {customers.Length} customers");
    }

    [Fact]
    public async Task GetAllAsync_Should_Return_All_Customers()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing GetAllAsync...");
        
        // Act
        var customers = await _repository.GetAllAsync();
        var customersList = customers.ToList();
        
        // Assert
        _output.WriteLine($"[TEST] Retrieved {customersList.Count} customers");
        Assert.NotNull(customersList);
        Assert.Equal(3, customersList.Count);
        
        // Afficher les détails
        foreach (var customer in customersList)
        {
            _output.WriteLine($"[TEST] Customer: {customer.FirstName} {customer.LastName} - {customer.Email}");
        }
    }

    [Fact]
    public async Task GetByIdAsync_Should_Return_Correct_Customer()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing GetByIdAsync...");
        var allCustomers = await _repository.GetAllAsync();
        var firstCustomer = allCustomers.First();
        
        // Act
        var customer = await _repository.GetByIdAsync(firstCustomer.Id);
        
        // Assert
        _output.WriteLine($"[TEST] Retrieved customer: {customer?.FirstName} {customer?.LastName}");
        Assert.NotNull(customer);
        Assert.Equal(firstCustomer.Id, customer.Id);
        Assert.Equal(firstCustomer.FirstName, customer.FirstName);
        Assert.Equal(firstCustomer.LastName, customer.LastName);
    }

    [Fact]
    public async Task AddAsync_Should_Add_New_Customer()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing AddAsync...");
        var newCustomer = new Customer
        {
            FirstName = "Nouveau",
            LastName = "Client",
            Email = "nouveau@test.com",
            Phone = "0612341234",
            BirthDate = new DateTime(2000, 1, 1),
            Address = "999 Rue Nouvelle",
            City = "Nice",
            PostalCode = "06000",
            Country = "France",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        // Act
        await _repository.AddAsync(newCustomer);
        await _unitOfWork.SaveChangesAsync();
        
        var allCustomers = await _repository.GetAllAsync();
        var customersList = allCustomers.ToList();
        
        // Assert
        _output.WriteLine($"[TEST] Total customers after add: {customersList.Count}");
        Assert.Equal(4, customersList.Count);
        
        var addedCustomer = customersList.FirstOrDefault(c => c.Email == "nouveau@test.com");
        Assert.NotNull(addedCustomer);
        _output.WriteLine($"[TEST] Added customer: {addedCustomer.FirstName} {addedCustomer.LastName}");
    }

    [Fact]
    public async Task SearchAsync_Should_Find_Customers_By_Name()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing SearchAsync...");
        var searchTerm = "ali";
        
        // Act
        var results = await _repository.SearchAsync(searchTerm);
        var resultsList = results.ToList();
        
        // Assert
        _output.WriteLine($"[TEST] Search for '{searchTerm}' found {resultsList.Count} customers");
        Assert.NotEmpty(resultsList);
        
        foreach (var customer in resultsList)
        {
            _output.WriteLine($"[TEST] Found: {customer.FirstName} {customer.LastName}");
            Assert.Contains(searchTerm, customer.FirstName.ToLower() + " " + customer.LastName.ToLower());
        }
    }

    public void Dispose()
    {
        _output.WriteLine("[TEST] Cleaning up test database...");
        _context.Database.EnsureDeleted();
        _context.Dispose();
        
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
            _output.WriteLine($"[TEST] Deleted test database: {_dbPath}");
        }
    }
}
