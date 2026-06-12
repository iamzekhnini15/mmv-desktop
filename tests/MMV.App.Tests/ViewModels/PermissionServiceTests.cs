using Moq;
using Xunit;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// Tests pour PermissionService (matrice de permissions par rôle).
/// </summary>
public class PermissionServiceTests
{
    private static (PermissionService service, Mock<ISessionService> mockSession) CreateService(UserRole role)
    {
        var mockSession = new Mock<ISessionService>();
        mockSession.Setup(s => s.IsAuthenticated).Returns(true);
        mockSession.Setup(s => s.CurrentUser).Returns(new User
        {
            UserId = 1, Username = "test", FirstName = "Test", LastName = "User",
            Role = role, IsActive = true, PasswordHash = "hash"
        });

        return (new PermissionService(mockSession.Object), mockSession);
    }

    #region Module access - Admin

    [Theory]
    [InlineData("Dashboard")]
    [InlineData("Customers")]
    [InlineData("Products")]
    [InlineData("Prescriptions")]
    [InlineData("Orders")]
    [InlineData("Sales")]
    [InlineData("Users")]
    [InlineData("Reports")]
    [InlineData("Settings")]
    public void Admin_CanAccessAllModules(string module)
    {
        var (service, _) = CreateService(UserRole.Admin);
        Assert.True(service.CanAccessModule(module));
    }

    #endregion

    #region Module access - Optician

    [Theory]
    [InlineData("Dashboard", true)]
    [InlineData("Customers", true)]
    [InlineData("Products", true)]
    [InlineData("Prescriptions", true)]
    [InlineData("Orders", true)]
    [InlineData("Sales", true)]
    [InlineData("Users", false)]
    [InlineData("Reports", true)]
    [InlineData("Settings", true)]
    public void Optician_ModuleAccess(string module, bool expected)
    {
        var (service, _) = CreateService(UserRole.Optician);
        Assert.Equal(expected, service.CanAccessModule(module));
    }

    #endregion

    #region Module access - Technician

    [Theory]
    [InlineData("Dashboard", true)]
    [InlineData("Customers", true)]
    [InlineData("Products", true)]
    [InlineData("Prescriptions", true)]
    [InlineData("Orders", true)]
    [InlineData("Sales", false)]
    [InlineData("Users", false)]
    [InlineData("Reports", false)]
    [InlineData("Settings", false)]
    public void Technician_ModuleAccess(string module, bool expected)
    {
        var (service, _) = CreateService(UserRole.Technician);
        Assert.Equal(expected, service.CanAccessModule(module));
    }

    #endregion

    #region CRUD permissions

    [Fact]
    public void Admin_CanCreateAll()
    {
        var (service, _) = CreateService(UserRole.Admin);
        Assert.True(service.CanCreate("customer"));
        Assert.True(service.CanCreate("product"));
        Assert.True(service.CanCreate("user"));
        Assert.True(service.CanCreate("sale"));
    }

    [Fact]
    public void Optician_CanCreateExceptUsers()
    {
        var (service, _) = CreateService(UserRole.Optician);
        Assert.True(service.CanCreate("customer"));
        Assert.True(service.CanCreate("product"));
        Assert.False(service.CanCreate("user"));
        Assert.True(service.CanCreate("sale"));
    }

    [Fact]
    public void Technician_CannotCreate()
    {
        var (service, _) = CreateService(UserRole.Technician);
        Assert.False(service.CanCreate("customer"));
        Assert.False(service.CanCreate("product"));
        Assert.False(service.CanCreate("user"));
        Assert.False(service.CanCreate("sale"));
    }

    [Fact]
    public void Admin_CanDeleteAll()
    {
        var (service, _) = CreateService(UserRole.Admin);
        Assert.True(service.CanDelete("customer"));
        Assert.True(service.CanDelete("product"));
        Assert.True(service.CanDelete("user"));
    }

    [Fact]
    public void Technician_CannotDelete()
    {
        var (service, _) = CreateService(UserRole.Technician);
        Assert.False(service.CanDelete("customer"));
        Assert.False(service.CanDelete("product"));
        Assert.False(service.CanDelete("user"));
    }

    #endregion

    #region Special permissions

    [Fact]
    public void Admin_CanRefund()
    {
        var (service, _) = CreateService(UserRole.Admin);
        Assert.True(service.CanRefund());
    }

    [Fact]
    public void Optician_CannotRefund()
    {
        var (service, _) = CreateService(UserRole.Optician);
        Assert.False(service.CanRefund());
    }

    [Fact]
    public void Technician_CannotRefund()
    {
        var (service, _) = CreateService(UserRole.Technician);
        Assert.False(service.CanRefund());
    }

    [Fact]
    public void AllRoles_CanChangeOrderStatus()
    {
        foreach (var role in new[] { UserRole.Admin, UserRole.Optician, UserRole.Technician })
        {
            var (service, _) = CreateService(role);
            Assert.True(service.CanChangeOrderStatus());
        }
    }

    [Fact]
    public void Admin_CanViewReports()
    {
        var (service, _) = CreateService(UserRole.Admin);
        Assert.True(service.CanViewReports());
    }

    [Fact]
    public void Technician_CannotViewReports()
    {
        var (service, _) = CreateService(UserRole.Technician);
        Assert.False(service.CanViewReports());
    }

    #endregion

    #region Unauthenticated

    [Fact]
    public void NotAuthenticated_CanAccessNothing()
    {
        var mockSession = new Mock<ISessionService>();
        mockSession.Setup(s => s.IsAuthenticated).Returns(false);
        mockSession.Setup(s => s.CurrentUser).Returns((User?)null);

        var service = new PermissionService(mockSession.Object);

        Assert.False(service.CanAccessModule("Dashboard"));
        Assert.False(service.CanCreate("customer"));
        Assert.False(service.CanEdit("customer"));
        Assert.False(service.CanDelete("customer"));
        Assert.False(service.CanViewReports());
        Assert.False(service.CanRefund());
    }

    #endregion
}
