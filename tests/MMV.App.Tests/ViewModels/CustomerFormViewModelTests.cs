using System;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using Xunit;
using Moq;

namespace MMV.App.Tests.ViewModels;

public class CustomerFormViewModelTests
{
    private readonly Mock<ICustomerRepository> _mockRepository;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly CustomerFormViewModel _viewModel;

    public CustomerFormViewModelTests()
    {
        _mockRepository = new Mock<ICustomerRepository>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _viewModel = new CustomerFormViewModel(_mockRepository.Object, _mockUnitOfWork.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeCommands()
    {
        // Arrange & Act
        var vm = new CustomerFormViewModel(_mockRepository.Object, _mockUnitOfWork.Object);

        // Assert
        Assert.NotNull(vm.SaveCommand);
        Assert.NotNull(vm.CancelCommand);
        Console.WriteLine("✅ TEST PASSED: Constructor initializes commands");
    }

    [Fact]
    public void InitializeForCreate_ShouldClearForm()
    {
        // Arrange
        _viewModel.FirstName = "Test";
        _viewModel.LastName = "User";
        _viewModel.Email = "test@example.com";

        // Act
        _viewModel.InitializeForCreate();

        // Assert
        Assert.Equal(string.Empty, _viewModel.FirstName);
        Assert.Equal(string.Empty, _viewModel.LastName);
        Assert.Equal(string.Empty, _viewModel.Email);
        Assert.False(_viewModel.IsEditMode);
        Console.WriteLine("✅ TEST PASSED: InitializeForCreate clears form");
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsFalseWhenMissingRequiredFields()
    {
        // Arrange
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "";
        _viewModel.LastName = "";

        // Act
        var canExecute = _viewModel.SaveCommand.CanExecute(null);

        // Assert
        Assert.False(canExecute);
        Console.WriteLine("✅ TEST PASSED: SaveCommand.CanExecute returns false when missing required fields");
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsTrueWhenRequiredFieldsFilled()
    {
        // Arrange
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";

        // Act
        var canExecute = _viewModel.SaveCommand.CanExecute(null);

        // Assert
        Assert.True(canExecute);
        Console.WriteLine("✅ TEST PASSED: SaveCommand.CanExecute returns true when required fields filled");
    }

    [Fact]
    public async Task SaveCommand_Execute_CreatesNewCustomer()
    {
        // Arrange
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";
        _viewModel.Email = "jean.dupont@example.com";
        _viewModel.Phone = "0612345678";

        Customer? savedCustomer = null;
        _mockRepository.Setup(r => r.CreateAsync(It.IsAny<Customer>(), default))
            .Callback<Customer, System.Threading.CancellationToken>((c, _) => savedCustomer = c)
            .ReturnsAsync((Customer c, System.Threading.CancellationToken ct) => c);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(default))
            .Returns(Task.FromResult(1));

        bool eventFired = false;
        _viewModel.CustomerSaved += (sender, customer) =>
        {
            eventFired = true;
            Console.WriteLine($"✅ CustomerSaved event fired for {customer.FirstName} {customer.LastName}");
        };

        // Act
        Console.WriteLine("🔵 Executing SaveCommand...");
        _viewModel.SaveCommand.Execute(null);

        // Wait a bit for async operation
        await Task.Delay(500);

        // Assert
        Assert.NotNull(savedCustomer);
        Assert.Equal("Jean", savedCustomer.FirstName);
        Assert.Equal("Dupont", savedCustomer.LastName);
        Assert.Equal("jean.dupont@example.com", savedCustomer.Email);
        Assert.True(eventFired);
        
        _mockRepository.Verify(r => r.CreateAsync(It.IsAny<Customer>(), default), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
        
        Console.WriteLine("✅ TEST PASSED: SaveCommand creates new customer and fires event");
    }

    [Fact]
    public async Task SaveCommand_Execute_UpdatesExistingCustomer()
    {
        // Arrange
        var existingCustomer = new Customer
        {
            CustomerId = 1,
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "jean@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };

        _viewModel.InitializeForEdit(existingCustomer);
        _viewModel.FirstName = "Jean Updated";
        _viewModel.Email = "jean.updated@example.com";

        _mockRepository.Setup(r => r.UpdateAsync(It.IsAny<Customer>(), default))
            .ReturnsAsync((Customer c, System.Threading.CancellationToken ct) => c);

        _mockUnitOfWork.Setup(u => u.SaveChangesAsync(default))
            .Returns(Task.FromResult(1));

        bool eventFired = false;
        _viewModel.CustomerSaved += (sender, customer) =>
        {
            eventFired = true;
            Console.WriteLine($"✅ CustomerSaved event fired for updated {customer.FirstName} {customer.LastName}");
        };

        // Act
        Console.WriteLine("🔵 Executing SaveCommand for update...");
        _viewModel.SaveCommand.Execute(null);

        await Task.Delay(500);

        // Assert
        Assert.True(eventFired);
        Assert.Equal("Jean Updated", existingCustomer.FirstName);
        Assert.Equal("jean.updated@example.com", existingCustomer.Email);
        
        _mockRepository.Verify(r => r.UpdateAsync(It.IsAny<Customer>(), default), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
        
        Console.WriteLine("✅ TEST PASSED: SaveCommand updates existing customer");
    }

    [Fact]
    public void CancelCommand_Execute_FiresCancelledEvent()
    {
        // Arrange
        bool eventFired = false;
        _viewModel.Cancelled += (sender, e) =>
        {
            eventFired = true;
            Console.WriteLine("✅ Cancelled event fired");
        };

        // Act
        _viewModel.CancelCommand.Execute(null);

        // Assert
        Assert.True(eventFired);
        Console.WriteLine("✅ TEST PASSED: CancelCommand fires Cancelled event");
    }

    [Fact]
    public void CancelCommand_CanExecute_ReturnsTrueWhenNotSaving()
    {
        // Arrange - Le formulaire est vide mais on devrait pouvoir annuler
        _viewModel.InitializeForCreate();

        // Act
        var canExecute = _viewModel.CancelCommand.CanExecute(null);

        // Assert
        Assert.True(canExecute);
        Console.WriteLine("✅ TEST PASSED: CancelCommand.CanExecute returns true when not saving (button should NOT be grayed out)");
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsFalseWhenFormIsEmpty()
    {
        // Arrange - Formulaire vide
        _viewModel.InitializeForCreate();

        // Act
        var canExecute = _viewModel.SaveCommand.CanExecute(null);

        // Assert
        Assert.False(canExecute);
        Console.WriteLine("✅ TEST PASSED: SaveCommand.CanExecute returns false when form is empty (button IS grayed out - EXPECTED BEHAVIOR)");
    }

    [Fact]
    public void SaveCommand_CanExecute_ChangesWhenFieldsAreFilled()
    {
        // Arrange
        _viewModel.InitializeForCreate();
        
        // Initially should be false
        Assert.False(_viewModel.SaveCommand.CanExecute(null));
        Console.WriteLine("🔵 Initial state: SaveCommand.CanExecute = false (expected)");

        // Act - Fill required fields
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";

        // Assert - Now should be true
        var canExecute = _viewModel.SaveCommand.CanExecute(null);
        Assert.True(canExecute);
        Console.WriteLine("✅ TEST PASSED: SaveCommand.CanExecute becomes true after filling required fields");
    }

    [Fact]
    public void InitializeForEdit_LoadsCustomerData()
    {
        // Arrange
        var customer = new Customer
        {
            CustomerId = 123,
            FirstName = "Marie",
            LastName = "Martin",
            Email = "marie@test.com",
            Phone = "0601020304",
            Address = "10 rue de Paris",
            City = "Lyon",
            PostalCode = "69000"
        };

        // Act
        _viewModel.InitializeForEdit(customer);

        // Assert
        Assert.Equal(123, _viewModel.CustomerId);
        Assert.Equal("Marie", _viewModel.FirstName);
        Assert.Equal("Martin", _viewModel.LastName);
        Assert.Equal("marie@test.com", _viewModel.Email);
        Assert.Equal("0601020304", _viewModel.Phone);
        Assert.Equal("10 rue de Paris", _viewModel.Address);
        Assert.Equal("Lyon", _viewModel.City);
        Assert.Equal("69000", _viewModel.PostalCode);
        Assert.True(_viewModel.IsEditMode);
        Console.WriteLine("✅ TEST PASSED: InitializeForEdit loads customer data correctly");
    }

    [Fact]
    public void InitializeForEdit_SaveCommandIsEnabled()
    {
        // Arrange - Un client existant avec des données valides
        var customer = new Customer
        {
            CustomerId = 123,
            FirstName = "Marie",
            LastName = "Martin"
        };

        // Act
        _viewModel.InitializeForEdit(customer);
        var canExecute = _viewModel.SaveCommand.CanExecute(null);

        // Assert - SaveCommand devrait être activé car FirstName et LastName sont remplis
        Assert.True(canExecute);
        Console.WriteLine("✅ TEST PASSED: SaveCommand is ENABLED after InitializeForEdit with valid data");
    }
}
