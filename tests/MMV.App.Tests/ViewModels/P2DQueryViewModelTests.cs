using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Notifications.CountUnreadNotifications;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;
using MMV.Application.UseCases.Suppliers.ListSuppliers;
using MMV.Application.UseCases.Users.ListUsers;
using MMV.Application.UseCases.Users.SetUserActive;
using MMV.Domain.Enums;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2D — Vérifie que les ViewModels de lecture migrés délèguent aux <i>query use cases</i> Application (au lieu d'un
/// repository), remplissent leur état d'écran depuis les DTO, conservent leurs filtres, et rejettent les dépendances
/// nulles. Ne teste pas le rendu Avalonia.
/// </summary>
public sealed class P2DQueryViewModelTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount;
        while (!condition() && Environment.TickCount - start < timeoutMs)
            await Task.Delay(10);
    }

    // ---------------- Utilisateurs (P2D-1) ----------------

    [Fact]
    public async Task UsersList_LoadsFromQueryUseCase_AndAppliesActiveFilter()
    {
        var list = new Mock<IListUsersUseCase>();
        list.Setup(u => u.ExecuteAsync(It.IsAny<ListUsersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserListItemDto>
            {
                new() { UserId = 1, Username = "actif", FirstName = "A", LastName = "A", Role = UserRole.Optician, IsActive = true },
                new() { UserId = 2, Username = "inactif", FirstName = "B", LastName = "B", Role = UserRole.Admin, IsActive = false },
            });

        var vm = new UsersListViewModel(list.Object, Mock.Of<ISetUserActiveUseCase>(), Mock.Of<IDialogService>());

        await WaitUntilAsync(() => vm.Users.Count == 2);
        list.Verify(u => u.ExecuteAsync(It.IsAny<ListUsersQuery>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Equal(2, vm.Users.Count);
        // ShowActiveOnly = true par défaut ⇒ seul l'utilisateur actif est affiché.
        Assert.Single(vm.FilteredUsers);
        Assert.Equal("actif", vm.FilteredUsers[0].Username);
    }

    [Fact]
    public void UsersList_Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new UsersListViewModel(null!, Mock.Of<ISetUserActiveUseCase>(), Mock.Of<IDialogService>()));
        Assert.Throws<ArgumentNullException>(() => _ = new UsersListViewModel(Mock.Of<IListUsersUseCase>(), null!, Mock.Of<IDialogService>()));
        Assert.Throws<ArgumentNullException>(() => _ = new UsersListViewModel(Mock.Of<IListUsersUseCase>(), Mock.Of<ISetUserActiveUseCase>(), null!));
    }

    // ---------------- Fournisseurs (P2D-2) ----------------

    [Fact]
    public async Task SuppliersList_LoadsFromQueryUseCase()
    {
        var list = new Mock<IListSuppliersUseCase>();
        list.Setup(u => u.ExecuteAsync(It.IsAny<ListSuppliersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SupplierListItemDto>
            {
                new() { SupplierId = 1, Name = "Alpha" },
                new() { SupplierId = 2, Name = "Beta" },
            });

        var vm = new SuppliersListViewModel(list.Object);

        await WaitUntilAsync(() => vm.Suppliers.Count == 2);
        Assert.Equal(2, vm.Suppliers.Count);
        Assert.Equal(2, vm.FilteredSuppliers.Count);
    }

    [Fact]
    public void SuppliersList_Constructor_RejectsNullDependency()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new SuppliersListViewModel(null!));
    }

    // ---------------- Notifications (P2D-3) ----------------

    [Fact]
    public async Task NotificationsList_LoadsListAndUnreadCount_FromQueryUseCases()
    {
        var list = new Mock<IListNotificationsUseCase>();
        list.Setup(u => u.ExecuteAsync(It.IsAny<ListNotificationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NotificationListItemDto>
            {
                new() { NotificationId = 1, Title = "N1", Message = "m", IsRead = false },
                new() { NotificationId = 2, Title = "N2", Message = "m", IsRead = true },
            });
        var count = new Mock<ICountUnreadNotificationsUseCase>();
        count.Setup(u => u.ExecuteAsync(It.IsAny<CountUnreadNotificationsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var vm = new NotificationsListViewModel(
            list.Object, count.Object,
            Mock.Of<IMarkAllNotificationsReadUseCase>(),
            Mock.Of<IGenerateLowStockNotificationsUseCase>());

        await WaitUntilAsync(() => vm.Notifications.Count == 2);
        Assert.Equal(2, vm.Notifications.Count);
        Assert.Equal(1, vm.UnreadCount);
    }

    [Fact]
    public void NotificationsList_Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => _ = new NotificationsListViewModel(
            null!, Mock.Of<ICountUnreadNotificationsUseCase>(), Mock.Of<IMarkAllNotificationsReadUseCase>(), Mock.Of<IGenerateLowStockNotificationsUseCase>()));
        Assert.Throws<ArgumentNullException>(() => _ = new NotificationsListViewModel(
            Mock.Of<IListNotificationsUseCase>(), null!, Mock.Of<IMarkAllNotificationsReadUseCase>(), Mock.Of<IGenerateLowStockNotificationsUseCase>()));
    }
}
