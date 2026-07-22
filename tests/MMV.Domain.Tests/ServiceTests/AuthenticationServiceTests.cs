using FluentAssertions;
using Moq;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
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

        _mockUserRepo.Setup(r => r.GetByNormalizedUsernameAsync("admin", It.IsAny<CancellationToken>()))
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

        _mockUserRepo.Setup(r => r.GetByNormalizedUsernameAsync("admin", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.AuthenticateAsync("admin", "WrongPass");

        result.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownUser_ReturnsNull()
    {
        _mockUserRepo.Setup(r => r.GetByNormalizedUsernameAsync("unknown", It.IsAny<CancellationToken>()))
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

        _mockUserRepo.Setup(r => r.GetByNormalizedUsernameAsync("admin", It.IsAny<CancellationToken>()))
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

    #region P3-10 — Politique de mot de passe et login normalisé

    /// <summary>
    /// P3-10 : un nouveau mot de passe faible est refusé par une exception métier typée, et <b>aucune écriture</b>
    /// n'a lieu — le hash existant reste intact. Avant P3-10, seule l'UI appliquait la politique : ce chemin
    /// remplaçait le hash par celui d'un mot de passe faible (audit §13, R1).
    /// </summary>
    [Theory]
    [InlineData("court1A")]      // moins de 8 caractères
    [InlineData("nouveaupass1")] // pas de majuscule
    [InlineData("NOUVEAUPASS1")] // pas de minuscule
    [InlineData("NouveauPass")]  // pas de chiffre
    public async Task ChangePasswordAsync_WeakNewPassword_ThrowsBusinessRule_AndKeepsExistingHash(string weakPassword)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("OldPass1!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", NormalizedUsername = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var act = async () => await _service.ChangePasswordAsync(1, "OldPass1!", weakPassword);

        await act.Should().ThrowAsync<BusinessRuleException>();

        user.PasswordHash.Should().Be(hash, "un refus ne doit jamais remplacer le hash existant");
        _mockUow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// L'ordre des gardes est délibéré : le mot de passe ACTUEL est vérifié avant la politique. Un appelant qui
    /// ne connaît pas le mot de passe actuel reçoit donc <c>false</c> — et non une exception qui lui apprendrait
    /// que sa proposition de nouveau mot de passe était, elle, acceptable.
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_WrongCurrentPassword_AndWeakNew_ReturnsFalse_WithoutRevealingPolicy()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("OldPass1!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", NormalizedUsername = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.ChangePasswordAsync(1, "WrongPassword", "weak");

        result.Should().BeFalse();
        user.PasswordHash.Should().Be(hash);
    }

    [Fact]
    public async Task ChangePasswordAsync_StrongNewPassword_ProducesNewVerifiableHash_WithNewSalt()
    {
        var originalHash = BCrypt.Net.BCrypt.HashPassword("OldPass1!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", NormalizedUsername = "admin", PasswordHash = originalHash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.ChangePasswordAsync(1, "OldPass1!", "NewStrongPass1");

        result.Should().BeTrue();
        user.PasswordHash.Should().NotBe(originalHash);
        user.PasswordHash.Should().NotBe("NewStrongPass1", "aucun clair n'est persisté");
        user.PasswordHash.Should().StartWith("$2a$11$", "le work factor 11 est inchangé");
        BCrypt.Net.BCrypt.Verify("NewStrongPass1", user.PasswordHash).Should().BeTrue();
    }

    /// <summary>
    /// Réutiliser le MÊME mot de passe reste autorisé (aucun historique n'est introduit en P3-10), mais produit
    /// un hash différent : BCrypt tire un nouveau sel à chaque hachage.
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_SamePassword_IsAllowed_ButProducesDifferentHash()
    {
        var originalHash = BCrypt.Net.BCrypt.HashPassword("OldPass1A", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", NormalizedUsername = "admin", PasswordHash = originalHash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.ChangePasswordAsync(1, "OldPass1A", "OldPass1A");

        result.Should().BeTrue();
        user.PasswordHash.Should().NotBe(originalHash, "nouveau sel ⇒ hash différent pour le même mot de passe");
        BCrypt.Net.BCrypt.Verify("OldPass1A", user.PasswordHash).Should().BeTrue();
    }

    [Fact]
    public void HashPassword_TwoUsersSamePassword_ProduceDifferentHashes()
    {
        var first = _service.HashPassword("SharedPass1");
        var second = _service.HashPassword("SharedPass1");

        first.Should().NotBe(second, "le sel est unique par hachage");
        _service.ValidatePassword("SharedPass1", first).Should().BeTrue();
        _service.ValidatePassword("SharedPass1", second).Should().BeTrue();
    }

    /// <summary>
    /// P3-10 : l'authentification recherche le compte par sa forme NORMALISÉE. Quelle que soit la casse ou les
    /// espaces périphériques saisis, c'est le même compte qui est atteint.
    /// </summary>
    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("  Admin  ")]
    public async Task AuthenticateAsync_CaseAndSpaceVariants_ReachTheSameAccount(string typedUsername)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("Admin123!", 11);
        var user = new User
        {
            UserId = 1, Username = "admin", NormalizedUsername = "admin", PasswordHash = hash,
            FirstName = "Admin", LastName = "User", Role = UserRole.Admin, IsActive = true
        };

        // Le port normalise lui-même son argument : le service lui transmet la saisie brute.
        _mockUserRepo.Setup(r => r.GetByNormalizedUsernameAsync(typedUsername, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);
        _mockUserRepo.Setup(r => r.GetByIdAsync(1L, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.AuthenticateAsync(typedUsername, "Admin123!");

        result.Should().NotBeNull();
        result!.NormalizedUsername.Should().Be("admin");
    }

    #endregion
}
