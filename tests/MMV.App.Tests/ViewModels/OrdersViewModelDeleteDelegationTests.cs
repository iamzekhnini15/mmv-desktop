using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.DeleteOrder;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2H — Comportement de présentation de <see cref="OrdersViewModel"/> après extraction du flux de
/// <b>suppression de commande</b> vers <see cref="IDeleteOrderUseCase"/>.
///
/// La VM ne supprime plus directement via <c>IOrderRepository.DeleteAsync</c> ni <c>IUnitOfWork.SaveChangesAsync</c> :
/// elle conserve la confirmation utilisateur, construit une <see cref="DeleteOrderCommand"/> et <b>délègue</b>.
/// Ces tests vérifient : (1) la délégation effective avec fermeture du détail ; (2) l'absence d'appel au use case
/// si l'utilisateur annule ; (3) le mapping état VM → commande (identifiant de commande) ; (4) l'affichage d'une
/// erreur propagée par le use case ; (5) le rejet d'un use case absent.
///
/// La couverture de persistance (vrai SQLite : suppression effective, introuvable) est portée par
/// <c>MMV.Application.Tests.DeleteOrderUseCaseTests</c>.
/// </summary>
public class OrdersViewModelDeleteDelegationTests
{
    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpyDeleteOrderUseCase : IDeleteOrderUseCase
    {
        private int _executeCount;
        private readonly Func<DeleteOrderCommand, Task<DeleteOrderResult>> _behavior;

        public SpyDeleteOrderUseCase(Func<DeleteOrderCommand, Task<DeleteOrderResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(new DeleteOrderResult { OrderFound = true, OrderId = cmd.OrderId }));

        public int ExecuteCount => _executeCount;
        public DeleteOrderCommand? LastCommand { get; private set; }

        public Task<DeleteOrderResult> ExecuteAsync(DeleteOrderCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }
    }

    private static readonly Order TestOrder = new()
    {
        OrderId = 42,
        OrderNumber = "CMD-TEST-001",
        Status = OrderStatus.New,
        Sale = new Sale
        {
            Customer = new Customer { FirstName = "Jean", LastName = "Dupont" },
            FinalAmount = 100m,
        },
    };

    /// <summary>
    /// Construit un <see cref="OrdersViewModel"/> complet avec mocks + spy, prêt pour déclencher le flux de
    /// suppression. La VM est mise dans l'état « detail showing » en déclenchant OnViewOrderDetail via la liste.
    /// </summary>
    private static async Task<(OrdersViewModel Vm, SpyDeleteOrderUseCase Spy)> BuildViewModelInDetailStateAsync(
        bool confirmDeletion,
        Func<DeleteOrderCommand, Task<DeleteOrderResult>>? behavior = null)
    {
        var spy = new SpyDeleteOrderUseCase(behavior);

        var orderRepo = new Mock<IOrderRepository>();
        // GetAllWithItemsAsync est appelé par OrdersListViewModel au démarrage.
        orderRepo.Setup(r => r.GetAllWithItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order>());
        // GetWithItemsAsync est appelé par OnViewOrderDetail pour charger le détail.
        orderRepo.Setup(r => r.GetWithItemsAsync(TestOrder.OrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestOrder);

        var unitOfWork = new Mock<IUnitOfWork>();
        var customerRepo = new Mock<ICustomerRepository>();
        var productRepo = new Mock<IProductRepository>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();
        var notificationRepo = new Mock<INotificationRepository>();
        var numberSequenceService = new Mock<INumberSequenceService>();
        var createOrderUseCase = new Mock<ICreateOrderUseCase>();
        var advanceOrderStatusUseCase = new Mock<IAdvanceOrderStatusUseCase>();
        var settleOrderBalanceUseCase = new Mock<ISettleOrderBalanceUseCase>();

        var dialogService = new Mock<IDialogService>();
        dialogService.Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(confirmDeletion);
        dialogService.Setup(d => d.ShowErrorAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var vm = new OrdersViewModel(
            orderRepo.Object,
            customerRepo.Object,
            productRepo.Object,
            prescriptionRepo.Object,
            notificationRepo.Object,
            unitOfWork.Object,
            dialogService.Object,
            numberSequenceService.Object,
            createOrderUseCase.Object,
            advanceOrderStatusUseCase.Object,
            settleOrderBalanceUseCase.Object,
            spy);

        // Déclencher OnViewOrderDetail : ViewDetailCommand → ViewOrderDetailRequested → OnViewOrderDetail (async void).
        vm.ListViewModel.ViewDetailCommand.Execute(TestOrder);

        // Attendre que DetailViewModel soit initialisé (OnViewOrderDetail async se termine).
        await WaitUntilAsync(() => vm.DetailViewModel != null);

        return (vm, spy);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    // ------------------------------------------------------------------
    // (1) Suppression confirmée : le use case est appelé et le détail est fermé
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WithConfirmation_DelegatesToUseCase_AndClosesDetail()
    {
        var (vm, spy) = await BuildViewModelInDetailStateAsync(confirmDeletion: true);

        Assert.True(vm.IsShowingDetail, "prérequis : le détail est affiché");

        // Déclencher le DeleteCommand sur le DetailViewModel (→ DeleteRequested → OnDeleteOrderRequested).
        vm.DetailViewModel!.DeleteCommand.Execute(null);

        // Attendre que la suppression soit traitée (CloseDetail ferme le détail).
        await WaitUntilAsync(() => !vm.IsShowingDetail);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.False(vm.IsShowingDetail, "le détail est fermé après suppression réussie");
        Assert.Null(vm.DetailViewModel);
        Assert.Null(vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (2) Suppression annulée : le use case n'est PAS appelé
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WhenUserCancels_DoesNotCallUseCase()
    {
        var (vm, spy) = await BuildViewModelInDetailStateAsync(confirmDeletion: false);

        vm.DetailViewModel!.DeleteCommand.Execute(null);

        // Laisser le temps à l'async void de s'exécuter (dialog retourne false immédiatement).
        await Task.Delay(200);

        Assert.Equal(0, spy.ExecuteCount);
        Assert.True(vm.IsShowingDetail, "le détail reste affiché si l'utilisateur annule");
        Assert.NotNull(vm.DetailViewModel);
    }

    // ------------------------------------------------------------------
    // (3) Mapping état VM → DeleteOrderCommand (identifiant de commande)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_BuildsCommandFromViewModelState()
    {
        var (vm, spy) = await BuildViewModelInDetailStateAsync(confirmDeletion: true);

        vm.DetailViewModel!.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => spy.ExecuteCount > 0);

        Assert.NotNull(spy.LastCommand);
        Assert.Equal(TestOrder.OrderId, spy.LastCommand!.OrderId);
    }

    // ------------------------------------------------------------------
    // (4) Exception propagée par le use case → ErrorMessage affiché, détail non fermé
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WhenUseCaseThrows_ShowsErrorMessage()
    {
        var (vm, spy) = await BuildViewModelInDetailStateAsync(
            confirmDeletion: true,
            behavior: _ => Task.FromException<DeleteOrderResult>(
                new InvalidOperationException("Erreur de suppression inattendue.")));

        vm.DetailViewModel!.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => vm.ErrorMessage != null);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.NotNull(vm.ErrorMessage);
        Assert.StartsWith("Erreur de suppression : ", vm.ErrorMessage);
        Assert.True(vm.IsShowingDetail, "le détail n'est pas fermé si la suppression échoue");
    }

    // ------------------------------------------------------------------
    // (5) Constructeur sans IDeleteOrderUseCase → ArgumentNullException
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutDeleteOrderUseCase_Throws()
    {
        var orderRepo = new Mock<IOrderRepository>();
        orderRepo.Setup(r => r.GetAllWithItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order>());

        var unitOfWork = new Mock<IUnitOfWork>();
        var customerRepo = new Mock<ICustomerRepository>();
        var productRepo = new Mock<IProductRepository>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();
        var notificationRepo = new Mock<INotificationRepository>();
        var numberSequenceService = new Mock<INumberSequenceService>();
        var createOrderUseCase = new Mock<ICreateOrderUseCase>();
        var advanceOrderStatusUseCase = new Mock<IAdvanceOrderStatusUseCase>();
        var settleOrderBalanceUseCase = new Mock<ISettleOrderBalanceUseCase>();
        var dialogService = new Mock<IDialogService>();

        Assert.Throws<ArgumentNullException>(() => new OrdersViewModel(
            orderRepo.Object,
            customerRepo.Object,
            productRepo.Object,
            prescriptionRepo.Object,
            notificationRepo.Object,
            unitOfWork.Object,
            dialogService.Object,
            numberSequenceService.Object,
            createOrderUseCase.Object,
            advanceOrderStatusUseCase.Object,
            settleOrderBalanceUseCase.Object,
            deleteOrderUseCase: null!));
    }
}
