using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.ListOrders;
using MMV.Domain.Enums;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2D-6 — Vérifie que les ViewModels de lecture du module Commandes (liste + Kanban) délèguent au query use case
/// Application <see cref="IListOrdersUseCase"/> (au lieu d'<c>IOrderRepository</c>), remplissent leur état d'écran
/// depuis les DTO plats, conservent filtres / répartition / recherche, propagent les erreurs, conservent l'avancement
/// de statut via <see cref="IAdvanceOrderStatusUseCase"/> et rejettent les dépendances nulles. Ne teste pas le rendu
/// Avalonia.
/// </summary>
public sealed class P2D6OrderReadViewModelTests
{
    private static OrderListItemDto Order(long id, string number, OrderStatus status, DateTime date,
        string firstName = "Jean", string lastName = "Dupont", decimal finalAmount = 100m, string? notes = null)
        => new()
        {
            OrderId = id,
            OrderNumber = number,
            Status = status,
            OrderDate = date,
            Notes = notes,
            Sale = new OrderSaleSummaryDto
            {
                FinalAmount = finalAmount,
                Customer = new OrderCustomerSummaryDto { FirstName = firstName, LastName = lastName },
            },
        };

    private static Mock<IListOrdersUseCase> ListReturning(params OrderListItemDto[] orders)
    {
        var useCase = new Mock<IListOrdersUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<ListOrdersQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);
        return useCase;
    }

    // ---------------- OrdersListViewModel ----------------

    [Fact]
    public async Task OrdersList_LoadsOrders_FromQueryUseCase()
    {
        var useCase = ListReturning(
            Order(1, "CMD-1", OrderStatus.New, new DateTime(2026, 6, 1)),
            Order(2, "CMD-2", OrderStatus.Delivered, new DateTime(2026, 1, 1)));

        var vm = new OrdersListViewModel(useCase.Object);
        await vm.LoadOrdersAsync();

        Assert.Equal(2, vm.Orders.Count);
        Assert.Equal(2, vm.FilteredOrders.Count);
        Assert.Equal(2, vm.TotalOrders);
        useCase.Verify(u => u.ExecuteAsync(It.IsAny<ListOrdersQuery>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task OrdersList_StatusFilter_IsPreserved()
    {
        var useCase = ListReturning(
            Order(1, "CMD-1", OrderStatus.New, new DateTime(2026, 6, 1)),
            Order(2, "CMD-2", OrderStatus.Delivered, new DateTime(2026, 1, 1)));

        var vm = new OrdersListViewModel(useCase.Object);
        await vm.LoadOrdersAsync();

        vm.SelectedStatus = "Livré"; // OrderStatus.Delivered

        Assert.Single(vm.FilteredOrders);
        Assert.Equal("CMD-2", vm.FilteredOrders[0].OrderNumber);
    }

    [Fact]
    public async Task OrdersList_Search_MatchesCustomerName()
    {
        var useCase = ListReturning(
            Order(1, "CMD-1", OrderStatus.New, new DateTime(2026, 6, 1), firstName: "Alice", lastName: "Martin"),
            Order(2, "CMD-2", OrderStatus.New, new DateTime(2026, 1, 1), firstName: "Bob", lastName: "Durand"));

        var vm = new OrdersListViewModel(useCase.Object);
        await vm.LoadOrdersAsync();

        vm.SearchText = "Martin";

        Assert.Single(vm.FilteredOrders);
        Assert.Equal("CMD-1", vm.FilteredOrders[0].OrderNumber);
    }

    [Fact]
    public async Task OrdersList_ViewDetail_RaisesEventWithDto()
    {
        var target = Order(42, "CMD-42", OrderStatus.New, new DateTime(2026, 6, 1));
        var useCase = ListReturning(target);
        var vm = new OrdersListViewModel(useCase.Object);
        await vm.LoadOrdersAsync();

        OrderListItemDto? raised = null;
        vm.ViewOrderDetailRequested += (_, dto) => raised = dto;

        vm.ViewDetailCommand.Execute(vm.FilteredOrders[0]);

        Assert.NotNull(raised);
        Assert.Equal(42, raised!.OrderId);
    }

    [Fact]
    public void OrdersList_Constructor_RejectsNullUseCase()
    {
        Assert.Throws<ArgumentNullException>(() => new OrdersListViewModel(null!));
    }

    // ---------------- OrderKanbanViewModel ----------------

    [Fact]
    public async Task Kanban_PartitionsOrdersByStatus()
    {
        var useCase = ListReturning(
            Order(1, "CMD-1", OrderStatus.New, new DateTime(2026, 6, 1)),
            Order(2, "CMD-2", OrderStatus.New, new DateTime(2026, 5, 1)),
            Order(3, "CMD-3", OrderStatus.InProgress, new DateTime(2026, 4, 1)));
        var advance = new Mock<IAdvanceOrderStatusUseCase>();

        var vm = new OrderKanbanViewModel(useCase.Object, advance.Object);
        await vm.LoadOrdersAsync();

        Assert.Equal(2, vm.NewOrders.Count);
        Assert.Equal(2, vm.NewCount);
        Assert.Single(vm.InProgressOrders);
        Assert.Equal(1, vm.InProgressCount);
    }

    [Fact]
    public async Task Kanban_AdvanceStatus_DelegatesToUseCase_WithNextStatus()
    {
        var target = Order(7, "CMD-7", OrderStatus.New, new DateTime(2026, 6, 1));
        var useCase = ListReturning(target);
        var advance = new Mock<IAdvanceOrderStatusUseCase>();
        advance.Setup(a => a.ExecuteAsync(It.IsAny<AdvanceOrderStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdvanceOrderStatusResult());

        var vm = new OrderKanbanViewModel(useCase.Object, advance.Object);
        await vm.LoadOrdersAsync();

        vm.AdvanceStatusCommand.Execute(vm.NewOrders[0]);
        await WaitUntilAsync(() =>
            advance.Invocations.Any(i => i.Method.Name == nameof(IAdvanceOrderStatusUseCase.ExecuteAsync)));

        advance.Verify(a => a.ExecuteAsync(
            It.Is<AdvanceOrderStatusCommand>(c => c.OrderId == 7 && c.NextStatus == OrderStatus.ToFabricate),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Kanban_Constructor_RejectsNullListUseCase()
    {
        var advance = new Mock<IAdvanceOrderStatusUseCase>();
        Assert.Throws<ArgumentNullException>(() => new OrderKanbanViewModel(null!, advance.Object));
    }

    [Fact]
    public void Kanban_Constructor_RejectsNullAdvanceUseCase()
    {
        var useCase = new Mock<IListOrdersUseCase>();
        Assert.Throws<ArgumentNullException>(() => new OrderKanbanViewModel(useCase.Object, null!));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var start = DateTime.UtcNow;
        while (!condition() && (DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }
}
