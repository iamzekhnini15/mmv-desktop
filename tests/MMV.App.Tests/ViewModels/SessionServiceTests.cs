using Moq;
using Xunit;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// Tests pour SessionService (login, logout, timer d'inactivité).
/// </summary>
public class SessionServiceTests : IDisposable
{
    private readonly SessionService _service = new();

    private static User CreateAdmin() => new()
    {
        UserId = 1, Username = "admin", FirstName = "Admin", LastName = "User",
        Role = UserRole.Admin, IsActive = true, PasswordHash = "hash"
    };

    private static User CreateOptician() => new()
    {
        UserId = 2, Username = "sophie", FirstName = "Sophie", LastName = "Martin",
        Role = UserRole.Optician, IsActive = true, PasswordHash = "hash"
    };

    [Fact]
    public void InitialState_NotAuthenticated()
    {
        Assert.Null(_service.CurrentUser);
        Assert.False(_service.IsAuthenticated);
        Assert.False(_service.IsAdmin);
    }

    [Fact]
    public void Login_SetsCurrentUser()
    {
        var user = CreateAdmin();
        _service.Login(user);

        Assert.NotNull(_service.CurrentUser);
        Assert.Equal("admin", _service.CurrentUser!.Username);
        Assert.True(_service.IsAuthenticated);
        Assert.True(_service.IsAdmin);
    }

    [Fact]
    public void Login_Optician_IsNotAdmin()
    {
        _service.Login(CreateOptician());

        Assert.True(_service.IsAuthenticated);
        Assert.False(_service.IsAdmin);
        Assert.True(_service.HasRole(UserRole.Optician));
        Assert.False(_service.HasRole(UserRole.Admin));
    }

    [Fact]
    public void Login_RaisesSessionChanged()
    {
        var raised = false;
        _service.SessionChanged += (s, e) => raised = true;

        _service.Login(CreateAdmin());

        Assert.True(raised);
    }

    [Fact]
    public void Logout_ClearsCurrentUser()
    {
        _service.Login(CreateAdmin());
        _service.Logout();

        Assert.Null(_service.CurrentUser);
        Assert.False(_service.IsAuthenticated);
        Assert.False(_service.IsAdmin);
    }

    [Fact]
    public void Logout_RaisesSessionChanged()
    {
        _service.Login(CreateAdmin());

        var raised = false;
        _service.SessionChanged += (s, e) => raised = true;

        _service.Logout();

        Assert.True(raised);
    }

    [Fact]
    public void Login_NullUser_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Login(null!));
    }

    [Fact]
    public void HasRole_NotAuthenticated_ReturnsFalse()
    {
        Assert.False(_service.HasRole(UserRole.Admin));
    }

    [Fact]
    public void ResetInactivityTimer_DoesNotThrow_WhenNotAuthenticated()
    {
        // Should not throw
        _service.ResetInactivityTimer();
    }

    [Fact]
    public void ResetInactivityTimer_DoesNotThrow_WhenAuthenticated()
    {
        _service.Login(CreateAdmin());
        // Should not throw
        _service.ResetInactivityTimer();
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
