using Xunit;
using MMV.App.ViewModels;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// Tests pour LoginViewModel.
/// </summary>
public class LoginViewModelTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var vm = new LoginViewModel();

        // Assert
        Assert.NotNull(vm.LoginCommand);
        Assert.Equal(string.Empty, vm.Username);
        Assert.Equal(string.Empty, vm.Password);
        Assert.Equal(string.Empty, vm.LoginError);
        Assert.False(vm.IsLoggingIn);
        Assert.Equal("Connexion - ManageMyVision", vm.Title);
    }

    [Fact]
    public void LoginCommand_CannotExecute_WhenUsernameIsEmpty()
    {
        // Arrange
        var vm = new LoginViewModel
        {
            Username = "",
            Password = "password123"
        };

        // Act & Assert
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void LoginCommand_CannotExecute_WhenPasswordIsEmpty()
    {
        // Arrange
        var vm = new LoginViewModel
        {
            Username = "admin",
            Password = ""
        };

        // Act & Assert
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenBothFieldsAreFilled()
    {
        // Arrange
        var vm = new LoginViewModel
        {
            Username = "admin",
            Password = "admin"
        };

        // Act & Assert
        Assert.True(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void LoginCommand_CannotExecute_WhenIsLoggingIn()
    {
        // Arrange
        var vm = new LoginViewModel
        {
            Username = "admin",
            Password = "admin"
        };

        // Simuler IsLoggingIn = true
        var isLoggingInProperty = typeof(LoginViewModel)
            .GetProperty(nameof(LoginViewModel.IsLoggingIn));
        isLoggingInProperty?.SetValue(vm, true);

        // Act & Assert
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExecuteLogin_Success_WithValidCredentials()
    {
        // Arrange
        var vm = new LoginViewModel
        {
            Username = "admin",
            Password = "admin"
        };
        
        var eventRaised = false;
        vm.LoginSuccessful += (s, e) => eventRaised = true;

        // Act
        vm.LoginCommand.Execute(null);
        
        // Attendre que l'authentification asynchrone se termine
        await Task.Delay(600);

        // Assert
        Assert.True(eventRaised, "LoginSuccessful event should be raised");
        Assert.Equal(string.Empty, vm.LoginError);
        Assert.False(vm.IsLoggingIn);
    }

    [Fact]
    public async Task ExecuteLogin_Failure_WithInvalidCredentials()
    {
        // Arrange
        var vm = new LoginViewModel
        {
            Username = "wronguser",
            Password = "wrongpass"
        };
        
        var eventRaised = false;
        vm.LoginSuccessful += (s, e) => eventRaised = true;

        // Act
        vm.LoginCommand.Execute(null);
        
        // Attendre que l'authentification asynchrone se termine
        await Task.Delay(600);

        // Assert
        Assert.False(eventRaised, "LoginSuccessful event should NOT be raised");
        Assert.Equal("Nom d'utilisateur ou mot de passe incorrect.", vm.LoginError);
        Assert.False(vm.IsLoggingIn);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenUsernameChanges()
    {
        // Arrange
        var vm = new LoginViewModel();
        var propertyChangedRaised = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LoginViewModel.Username))
                propertyChangedRaised = true;
        };

        // Act
        vm.Username = "testuser";

        // Assert
        Assert.True(propertyChangedRaised);
        Assert.Equal("testuser", vm.Username);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenPasswordChanges()
    {
        // Arrange
        var vm = new LoginViewModel();
        var propertyChangedRaised = false;
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LoginViewModel.Password))
                propertyChangedRaised = true;
        };

        // Act
        vm.Password = "testpass";

        // Assert
        Assert.True(propertyChangedRaised);
        Assert.Equal("testpass", vm.Password);
    }
}
