using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.CreateCustomer;
using MMV.Application.UseCases.Customers.UpdateCustomer;
using MMV.Domain.Entities;
using Xunit;
using Moq;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-2 — La <see cref="CustomerFormViewModel"/> ne persiste plus directement : elle <b>délègue</b> à
/// <see cref="ICreateCustomerUseCase"/> (création) et <see cref="IUpdateCustomerUseCase"/> (édition) de la couche
/// Application. Ces tests vérifient la délégation, le mapping état → commande, les garde-fous (double-submit,
/// remontée d'erreur) et le comportement d'écran inchangé (validation de surface, événements).
/// </summary>
public class CustomerFormViewModelTests
{
    private readonly Mock<ICreateCustomerUseCase> _mockCreate;
    private readonly Mock<IUpdateCustomerUseCase> _mockUpdate;
    private readonly CustomerFormViewModel _viewModel;

    public CustomerFormViewModelTests()
    {
        _mockCreate = new Mock<ICreateCustomerUseCase>();
        _mockUpdate = new Mock<IUpdateCustomerUseCase>();
        _viewModel = new CustomerFormViewModel(_mockCreate.Object, _mockUpdate.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeCommands()
    {
        var vm = new CustomerFormViewModel(_mockCreate.Object, _mockUpdate.Object);

        Assert.NotNull(vm.SaveCommand);
        Assert.NotNull(vm.CancelCommand);
    }

    [Fact]
    public void Constructor_NullCreateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CustomerFormViewModel(null!, _mockUpdate.Object));
    }

    [Fact]
    public void Constructor_NullUpdateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CustomerFormViewModel(_mockCreate.Object, null!));
    }

    [Fact]
    public void InitializeForCreate_ShouldClearForm()
    {
        _viewModel.FirstName = "Test";
        _viewModel.LastName = "User";
        _viewModel.Email = "test@example.com";

        _viewModel.InitializeForCreate();

        Assert.Equal(string.Empty, _viewModel.FirstName);
        Assert.Equal(string.Empty, _viewModel.LastName);
        Assert.Equal(string.Empty, _viewModel.Email);
        Assert.False(_viewModel.IsEditMode);
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsFalseWhenMissingRequiredFields()
    {
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "";
        _viewModel.LastName = "";

        Assert.False(_viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsTrueWhenRequiredFieldsFilled()
    {
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";

        Assert.True(_viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveCommand_Execute_DelegatesToCreateUseCase_AndFiresEvent()
    {
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";
        _viewModel.Email = "jean.dupont@example.com";
        _viewModel.Phone = "0612345678";

        CreateCustomerCommand? captured = null;
        _mockCreate
            .Setup(u => u.ExecuteAsync(It.IsAny<CreateCustomerCommand>(), It.IsAny<CancellationToken>()))
            .Callback<CreateCustomerCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(new CreateCustomerResult { CustomerId = 42, DisplayName = "Jean Dupont" });

        Customer? savedCustomer = null;
        _viewModel.CustomerSaved += (_, customer) => savedCustomer = customer;

        _viewModel.SaveCommand.Execute(null);
        await Task.Delay(100);

        // Délégation au bon use case, une seule fois ; aucune écriture côté édition.
        _mockCreate.Verify(u => u.ExecuteAsync(It.IsAny<CreateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockUpdate.Verify(u => u.ExecuteAsync(It.IsAny<UpdateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        // Mapping état → commande.
        Assert.NotNull(captured);
        Assert.Equal("Jean", captured!.FirstName);
        Assert.Equal("Dupont", captured.LastName);
        Assert.Equal("jean.dupont@example.com", captured.Email);
        Assert.Equal("0612345678", captured.Phone);

        // Événement de succès avec l'identifiant attribué par le use case.
        Assert.NotNull(savedCustomer);
        Assert.Equal(42, savedCustomer!.CustomerId);
        Assert.False(_viewModel.IsSaving);
    }

    [Fact]
    public async Task SaveCommand_Execute_DelegatesToUpdateUseCase_WhenEditing()
    {
        var existingCustomer = new Customer
        {
            CustomerId = 7,
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "jean@example.com",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-10)
        };

        _viewModel.InitializeForEdit(existingCustomer);
        _viewModel.FirstName = "Jean Updated";
        _viewModel.Email = "jean.updated@example.com";

        UpdateCustomerCommand? captured = null;
        _mockUpdate
            .Setup(u => u.ExecuteAsync(It.IsAny<UpdateCustomerCommand>(), It.IsAny<CancellationToken>()))
            .Callback<UpdateCustomerCommand, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(new UpdateCustomerResult { CustomerFound = true, CustomerId = 7, DisplayName = "Jean Updated Dupont" });

        bool eventFired = false;
        _viewModel.CustomerSaved += (_, _) => eventFired = true;

        _viewModel.SaveCommand.Execute(null);
        await Task.Delay(100);

        _mockUpdate.Verify(u => u.ExecuteAsync(It.IsAny<UpdateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockCreate.Verify(u => u.ExecuteAsync(It.IsAny<CreateCustomerCommand>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.CustomerId);
        Assert.Equal("Jean Updated", captured.FirstName);
        Assert.Equal("jean.updated@example.com", captured.Email);
        Assert.True(eventFired);
        Assert.False(_viewModel.IsSaving);
    }

    [Fact]
    public async Task SaveCommand_Execute_UpdateNotFound_ShowsError_AndDoesNotFireSaved()
    {
        var existingCustomer = new Customer { CustomerId = 99, FirstName = "Ghost", LastName = "User" };
        _viewModel.InitializeForEdit(existingCustomer);

        _mockUpdate
            .Setup(u => u.ExecuteAsync(It.IsAny<UpdateCustomerCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCustomerResult { CustomerFound = false, CustomerId = 99 });

        bool eventFired = false;
        _viewModel.CustomerSaved += (_, _) => eventFired = true;

        _viewModel.SaveCommand.Execute(null);
        await Task.Delay(100);

        Assert.False(eventFired);
        Assert.False(string.IsNullOrEmpty(_viewModel.ErrorMessage));
        Assert.False(_viewModel.IsSaving);
    }

    [Fact]
    public async Task SaveCommand_Execute_UseCaseThrows_SetsErrorMessage_AndResetsIsSaving()
    {
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";

        _mockCreate
            .Setup(u => u.ExecuteAsync(It.IsAny<CreateCustomerCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        _viewModel.SaveCommand.Execute(null);
        await Task.Delay(100);

        Assert.Contains("boom", _viewModel.ErrorMessage);
        Assert.False(_viewModel.IsSaving);
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsFalseWhileSaving_PreventsDoubleSubmit()
    {
        _viewModel.InitializeForCreate();
        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";
        Assert.True(_viewModel.SaveCommand.CanExecute(null));

        _viewModel.IsSaving = true;

        Assert.False(_viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommand_Execute_FiresCancelledEvent()
    {
        bool eventFired = false;
        _viewModel.Cancelled += (_, _) => eventFired = true;

        _viewModel.CancelCommand.Execute(null);

        Assert.True(eventFired);
    }

    [Fact]
    public void CancelCommand_CanExecute_ReturnsTrueWhenNotSaving()
    {
        _viewModel.InitializeForCreate();

        Assert.True(_viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_CanExecute_ReturnsFalseWhenFormIsEmpty()
    {
        _viewModel.InitializeForCreate();

        Assert.False(_viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void SaveCommand_CanExecute_ChangesWhenFieldsAreFilled()
    {
        _viewModel.InitializeForCreate();
        Assert.False(_viewModel.SaveCommand.CanExecute(null));

        _viewModel.FirstName = "Jean";
        _viewModel.LastName = "Dupont";

        Assert.True(_viewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void InitializeForEdit_LoadsCustomerData()
    {
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

        _viewModel.InitializeForEdit(customer);

        Assert.Equal(123, _viewModel.CustomerId);
        Assert.Equal("Marie", _viewModel.FirstName);
        Assert.Equal("Martin", _viewModel.LastName);
        Assert.Equal("marie@test.com", _viewModel.Email);
        Assert.Equal("0601020304", _viewModel.Phone);
        Assert.Equal("10 rue de Paris", _viewModel.Address);
        Assert.Equal("Lyon", _viewModel.City);
        Assert.Equal("69000", _viewModel.PostalCode);
        Assert.True(_viewModel.IsEditMode);
    }

    [Fact]
    public void InitializeForEdit_SaveCommandIsEnabled()
    {
        var customer = new Customer
        {
            CustomerId = 123,
            FirstName = "Marie",
            LastName = "Martin"
        };

        _viewModel.InitializeForEdit(customer);

        Assert.True(_viewModel.SaveCommand.CanExecute(null));
    }
}
