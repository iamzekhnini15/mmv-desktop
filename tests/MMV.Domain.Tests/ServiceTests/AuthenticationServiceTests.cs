using FluentAssertions;
using Moq;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Domain.Tests.ServiceTests;

/// <summary>
/// Tests pour AuthenticationService (BCrypt, login, changement de mot de passe).
/// </summary>
public class AuthenticationServiceTests
{
    private readonly Mock<IUnitOfWork> _mockUow;
    private readonly Mock<IUserRepository> _mockUserRepo;
    private readonly AuthenticationService _service;

    public AuthenticationServiceTests()
    {
        _mockUserRepo = new Mock<IUserRepository>();
        _mockUow = new Mock<IUnitOfWork>();
        _mockUow.Setup(u => u.Users).Returns(_mockUserRepo.Object);
        _service = new AuthenticationService(_mockUow.Object);
    }

    #region HashPassword / ValidatePassword

    [Fact]
    public void HashPassword_ProducesValidBCryptHash()
    {
        var hash = _service.HashPassword("TestPassword1");
        hash.Should().StartWith("$2a$11$");
    }

    [Fact]
    public void ValidatePassword_CorrectPassword_ReturnsTrue()
    {
        var hash = _service.HashPassword("MyPassword1");
        _service.ValidatePassword("MyPassword1", hash).Should().BeTrue();
    }

    [Fact]
    public void ValidatePassword_WrongPassword_ReturnsFalse()
    {
        var hash = _service.HashPassword("MyPassword1");
        _service.ValidatePassword("WrongPassword", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "hash")]
    [InlineData("", "hash")]
    [InlineData("password", null)]
    [InlineData("password", "")]
    public void ValidatePassword_NullOrEmpty_ReturnsFalse(string? password, string? hash)
    {
        _service.ValidatePassword(password!, hash!).Should().BeFalse();
    }

    [Fact]
    public void ValidatePassword_InvalidHash_ReturnsFalse()
    {
        _service.ValidatePassword("password", "not-a-valid-bcrypt-hash").Should().BeFalse();
    }

    [Fact]
    public void HashPassword_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => _service.HashPassword(""));
    }

    #endregion

    #region AuthenticateAsync

    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsUser()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Admin123!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByUsernameAsync("admin", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.AuthenticateAsync("admin", "Admin123!");

        result.Should().NotBeNull();
        result!.Username.Should().Be("admin");
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidPassword_ReturnsNull()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Admin123!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByUsernameAsync("admin", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.AuthenticateAsync("admin", "WrongPass");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownUser_ReturnsNull()
    {
        _mockUserRepo.Setup(r => r.GetByUsernameAsync("unknown", It.IsAny<CancellationToken>()))
                     .ReturnsAsync((User?)null);

        var result = await _service.AuthenticateAsync("unknown", "password");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_InactiveUser_ReturnsNull()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Admin123!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = false
        };

        _mockUserRepo.Setup(r => r.GetByUsernameAsync("admin", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.AuthenticateAsync("admin", "Admin123!");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_EmptyUsername_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AuthenticateAsync("", "password"));
    }

    [Fact]
    public async Task AuthenticateAsync_EmptyPassword_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AuthenticateAsync("admin", ""));
    }

    #endregion

    #region ChangePasswordAsync

    [Fact]
    public async Task ChangePasswordAsync_ValidOldPassword_ReturnsTrue()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("OldPass1!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.ChangePasswordAsync(1, "OldPass1!", "NewPass2!");

        result.Should().BeTrue();
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongOldPassword_ReturnsFalse()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("OldPass1!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.ChangePasswordAsync(1, "WrongPassword", "NewPass2!");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangePasswordAsync_UnknownUser_ReturnsFalse()
    {
        _mockUserRepo.Setup(r => r.GetByIdAsync(999L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync((User?)null);

        var result = await _service.ChangePasswordAsync(999, "OldPass1!", "NewPass2!");

        result.Should().BeFalse();
    }

    #endregion
}
