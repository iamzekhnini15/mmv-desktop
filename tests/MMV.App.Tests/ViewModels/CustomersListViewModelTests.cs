using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// Tests pour CustomersListViewModel.
/// </summary>
public class CustomersListViewModelTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ICustomerRepository> _mockRepository;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;

    public CustomersListViewModelTests(ITestOutputHelper output)
    {
        _output = output;
        _mockRepository = new Mock<ICustomerRepository>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();
    }

    private List<Customer> GetTestCustomers()
    {
        return new List<Customer>
        {
            new Customer
            {
                Id = 1,
                FirstName = "Ali",
                LastName = "Zekhnini",
                Email = "ali@test.com",
                Phone = "0612345678",
                BirthDate = new DateTime(1990, 1, 1),
                City = "Paris",
                CreatedAt = DateTime.UtcNow
            },
            new Customer
            {
                Id = 2,
                FirstName = "Marie",
                LastName = "Dupont",
                Email = "marie@test.com",
                Phone = "0698765432",
                BirthDate = new DateTime(1985, 5, 15),
                City = "Lyon",
                CreatedAt = DateTime.UtcNow
            },
            new Customer
            {
                Id = 3,
                FirstName = "Jean",
                LastName = "Martin",
                Email = "jean@test.com",
                Phone = "0687654321",
                BirthDate = new DateTime(1975, 12, 25),
                City = "Marseille",
                CreatedAt = DateTime.UtcNow
            }
        };
    }

    [Fact]
    public async Task LoadCustomersAsync_Should_Load_All_Customers()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing LoadCustomersAsync...");
        var testCustomers = GetTestCustomers();
        
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(testCustomers);

        var viewModel = new CustomersListViewModel(_mockRepository.Object, _mockUnitOfWork.Object);
        
        // Wait a bit for async loading to complete
        await Task.Delay(500);

        // Assert
        _output.WriteLine($"[TEST] Customers.Count = {viewModel.Customers.Count}");
        _output.WriteLine($"[TEST] FilteredCustomers.Count = {viewModel.FilteredCustomers.Count}");
        _output.WriteLine($"[TEST] TotalCustomers = {viewModel.TotalCustomers}");
        
        Assert.Equal(3, viewModel.Customers.Count);
        Assert.Equal(3, viewModel.FilteredCustomers.Count);
        Assert.Equal(3, viewModel.TotalCustomers);
        
        foreach (var customer in viewModel.FilteredCustomers)
        {
            _output.WriteLine($"[TEST] Customer: {customer.FirstName} {customer.LastName} - {customer.Email}");
        }
    }

    [Fact]
    public async Task SearchText_Should_Filter_Customers()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing SearchText filtering...");
        var testCustomers = GetTestCustomers();
        
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(testCustomers);

        var viewModel = new CustomersListViewModel(_mockRepository.Object, _mockUnitOfWork.Object);
        await Task.Delay(500); // Wait for initial load

        // Act
        _output.WriteLine("[TEST] Setting SearchText to 'ali'");
        viewModel.SearchText = "ali";

        // Assert
        _output.WriteLine($"[TEST] FilteredCustomers.Count after search = {viewModel.FilteredCustomers.Count}");
        Assert.Single(viewModel.FilteredCustomers);
        Assert.Equal("Ali", viewModel.FilteredCustomers.First().FirstName);
        
        foreach (var customer in viewModel.FilteredCustomers)
        {
            _output.WriteLine($"[TEST] Filtered Customer: {customer.FirstName} {customer.LastName}");
        }
    }

    [Fact]
    public async Task CreateCommand_Should_Raise_CreateCustomerRequested_Event()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing CreateCommand...");
        var testCustomers = GetTestCustomers();
        
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(testCustomers);

        var viewModel = new CustomersListViewModel(_mockRepository.Object, _mockUnitOfWork.Object);
        await Task.Delay(500);

        bool eventRaised = false;
        viewModel.CreateCustomerRequested += (sender, args) =>
        {
            _output.WriteLine("[TEST] CreateCustomerRequested event raised!");
            eventRaised = true;
        };

        // Act
        _output.WriteLine("[TEST] Executing CreateCommand...");
        viewModel.CreateCommand.Execute(null);

        // Assert
        Assert.True(eventRaised, "CreateCustomerRequested event should have been raised");
    }

    [Fact]
    public async Task EditCommand_Should_Raise_EditCustomerRequested_Event()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing EditCommand...");
        var testCustomers = GetTestCustomers();
        
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(testCustomers);

        var viewModel = new CustomersListViewModel(_mockRepository.Object, _mockUnitOfWork.Object);
        await Task.Delay(500);

        Customer? editedCustomer = null;
        viewModel.EditCustomerRequested += (sender, customer) =>
        {
            _output.WriteLine($"[TEST] EditCustomerRequested event raised for {customer.FirstName} {customer.LastName}");
            editedCustomer = customer;
        };

        // Act
        viewModel.SelectedCustomer = testCustomers.First();
        _output.WriteLine($"[TEST] Selected customer: {viewModel.SelectedCustomer.FirstName} {viewModel.SelectedCustomer.LastName}");
        _output.WriteLine("[TEST] Executing EditCommand...");
        viewModel.EditCommand.Execute(null);

        // Assert
        Assert.NotNull(editedCustomer);
        Assert.Equal(testCustomers.First().Id, editedCustomer.Id);
    }

    [Fact]
    public async Task FilteredCustomers_Should_Update_When_Customers_Change()
    {
        // Arrange
        _output.WriteLine("[TEST] Testing FilteredCustomers update...");
        var initialCustomers = GetTestCustomers();
        
        _mockRepository.Setup(r => r.GetAllAsync())
            .ReturnsAsync(initialCustomers);

        var viewModel = new CustomersListViewModel(_mockRepository.Object, _mockUnitOfWork.Object);
        await Task.Delay(500);

        _output.WriteLine($"[TEST] Initial FilteredCustomers.Count = {viewModel.FilteredCustomers.Count}");
        Assert.Equal(3, viewModel.FilteredCustomers.Count);

        // Act - Search for specific customer
        viewModel.SearchText = "Marie";

        // Assert
        _output.WriteLine($"[TEST] After search 'Marie', FilteredCustomers.Count = {viewModel.FilteredCustomers.Count}");
        Assert.Single(viewModel.FilteredCustomers);
        Assert.Equal("Marie", viewModel.FilteredCustomers.First().FirstName);
    }
}
