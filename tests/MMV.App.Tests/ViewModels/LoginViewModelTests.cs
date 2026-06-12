using Moq;
using Xunit;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Services;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// Tests pour LoginViewModel avec mocks d'authentification.
/// </summary>
public class LoginViewModelTests
{
    private readonly Mock<IAuthenticationService> _mockAuth;
    private readonly Mock<ISessionService> _mockSession;

    public LoginViewModelTests()
    {
        _mockAuth = new Mock<IAuthenticationService>();
        _mockSession = new Mock<ISessionService>();
    }

    private LoginViewModel CreateViewModel() => new(_mockAuth.Object, _mockSession.Object);

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var vm = CreateViewModel();

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
        var vm = CreateViewModel();
        vm.Username = "";
        vm.Password = "password123";

        // Act & Assert
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void LoginCommand_CannotExecute_WhenPasswordIsEmpty()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.Username = "admin";
        vm.Password = "";

        // Act & Assert
        Assert.False(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void LoginCommand_CanExecute_WhenBothFieldsAreFilled()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.Username = "admin";
        vm.Password = "admin";

        // Act & Assert
        Assert.True(vm.LoginCommand.CanExecute(null));
    }

    [Fact]
    public void LoginCommand_CannotExecute_WhenIsLoggingIn()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.Username = "admin";
        vm.Password = "admin";

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
        var fakeUser = new User
        {
            UserId = 1,
            Username = "admin",
            FirstName = "Admin",
            LastName = "User",
            Role = UserRole.Admin,
            IsActive = true,
            PasswordHash = "hash"
        };

        _mockAuth.Setup(a => a.AuthenticateAsync("admin", "admin", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(fakeUser);

        var vm = CreateViewModel();
        vm.Username = "admin";
        vm.Password = "admin";

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
        _mockSession.Verify(s => s.Login(fakeUser), Times.Once);
    }

    [Fact]
    public async Task ExecuteLogin_Failure_WithInvalidCredentials()
    {
        // Arrange
        _mockAuth.Setup(a => a.AuthenticateAsync("wronguser", "wrongpass", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((User?)null);

        var vm = CreateViewModel();
        vm.Username = "wronguser";
        vm.Password = "wrongpass";

        var eventRaised = false;
        vm.LoginSuccessful += (s, e) => eventRaised = true;

        // Act
        vm.LoginCommand.Execute(null);

        // Attendre que l'authentification asynchrone se termine
        await Task.Delay(600);

        // Assert
        Assert.False(eventRaised, "LoginSuccessful event should NOT be raised");
        Assert.Contains("incorrect", vm.LoginError);
        Assert.False(vm.IsLoggingIn);
    }

    [Fact]
    public void PropertyChanged_IsRaised_WhenUsernameChanges()
    {
        // Arrange
        var vm = CreateViewModel();
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
        var vm = CreateViewModel();
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
