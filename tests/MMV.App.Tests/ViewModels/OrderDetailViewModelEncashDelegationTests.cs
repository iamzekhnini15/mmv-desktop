using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2G — Comportement de présentation de <see cref="OrderDetailViewModel"/> après extraction du flux
/// d'<b>encaissement du solde</b> vers <see cref="ISettleOrderBalanceUseCase"/>.
///
/// La VM ne recharge plus la commande, ne met plus directement à jour le paiement de la vente, ne crée plus la
/// notification et n'enregistre plus elle-même : elle mappe son état (identifiant de commande) vers une
/// <see cref="SettleOrderBalanceCommand"/> et <b>délègue</b>. Ces tests vérifient : (1) la délégation effective
/// + l'événement <c>OrderUpdated</c> ; (2) le message « Commande introuvable. » sans mise à jour d'état ;
/// (3) l'affichage d'une erreur propagée par le use case ; (4) le rejet d'un use case absent ; (5) le mapping
/// état VM → commande ; (6) la garde d'affichage <c>HasRemainingBalance</c> (pas d'encaissement sans solde).
///
/// La couverture de persistance (vrai SQLite : paiement de la vente, notification, rollback transactionnel) est
/// portée par <c>MMV.Application.Tests.SettleOrderBalanceUseCaseTests</c>.
/// </summary>
public class OrderDetailViewModelEncashDelegationTests
{
    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpySettleOrderBalanceUseCase : ISettleOrderBalanceUseCase
    {
        private int _executeCount;
        private readonly Func<SettleOrderBalanceCommand, Task<SettleOrderBalanceResult>> _behavior;

        public SpySettleOrderBalanceUseCase(Func<SettleOrderBalanceCommand, Task<SettleOrderBalanceResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(SuccessResult(cmd)));

        public int ExecuteCount => _executeCount;
        public SettleOrderBalanceCommand? LastCommand { get; private set; }

        public Task<SettleOrderBalanceResult> ExecuteAsync(SettleOrderBalanceCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }

        /// <summary>Résultat de succès : commande soldée (solde restant nul) avec sa vente mise à jour.</summary>
        public static SettleOrderBalanceResult SuccessResult(SettleOrderBalanceCommand cmd) => new()
        {
            OrderFound = true,
            Order = new Order
            {
                OrderId = cmd.OrderId,
                OrderNumber = "CMD-FRESH",
                Sale = new Sale { FinalAmount = 100m, DepositAmount = 100m, RemainingAmount = 0m },
            },
            AmountEncashed = 60m,
            OldRemainingAmount = 60m,
            NewRemainingAmount = 0m,
            IsFullyPaid = true,
            HasNotification = true,
        };
    }

    /// <summary>Construit une VM initialisée sur une commande avec un solde restant à encaisser.</summary>
    private static OrderDetailViewModel BuildInitializedViewModel(ISettleOrderBalanceUseCase settleUseCase)
    {
        var orderRepo = new Mock<IOrderRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var advanceUseCase = new Mock<IAdvanceOrderStatusUseCase>();
        var notificationRepo = new Mock<INotificationRepository>();

        var viewModel = new OrderDetailViewModel(
            orderRepo.Object, unitOfWork.Object, advanceUseCase.Object, settleUseCase, notificationRepo.Object);

        var order = new Order
        {
            OrderId = 42,
            OrderNumber = "CMD-000500",
            Status = OrderStatus.Ready,
            Sale = new Sale
            {
                Customer = new Customer { FirstName = "Jean", LastName = "Dupont" },
                FinalAmount = 100m,
                DepositAmount = 40m,
                RemainingAmount = 60m,
            },
        };
        order.OrderItems.Add(new OrderItem { ItemType = OrderItemType.LensOd, Quantity = 1 });

        viewModel.Initialize(order);
        return viewModel;
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
    // (1) L'encaissement délègue au use case et notifie OrderUpdated
    // ------------------------------------------------------------------

    [Fact]
    public async Task Encash_DelegatesToUseCase_AndRaisesOrderUpdated()
    {
        var spy = new SpySettleOrderBalanceUseCase();
        var viewModel = BuildInitializedViewModel(spy);

        Order? updatedWith = null;
        viewModel.OrderUpdated += (_, order) => updatedWith = order;

        viewModel.EncashBalanceCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.NotNull(updatedWith);
        Assert.Equal("CMD-FRESH", viewModel.Order!.OrderNumber);
        Assert.False(viewModel.HasRemainingBalance, "après encaissement, plus de solde restant");
        Assert.Null(viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (2) Commande introuvable : message dédié, aucune mise à jour d'état
    // ------------------------------------------------------------------

    [Fact]
    public async Task Encash_WhenOrderNotFound_ShowsNotFoundMessage_AndDoesNotUpdate()
    {
        var spy = new SpySettleOrderBalanceUseCase(_ => Task.FromResult(new SettleOrderBalanceResult { OrderFound = false }));
        var viewModel = BuildInitializedViewModel(spy);

        var updated = false;
        viewModel.OrderUpdated += (_, _) => updated = true;

        viewModel.EncashBalanceCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal("Commande introuvable.", viewModel.ErrorMessage);
        Assert.False(updated, "aucune mise à jour d'état si la commande est introuvable");
        Assert.Equal("CMD-000500", viewModel.Order!.OrderNumber);
        Assert.True(viewModel.HasRemainingBalance, "le solde reste à encaisser si introuvable");
    }

    // ------------------------------------------------------------------
    // (3) Exception propagée par le use case → message d'erreur affiché
    // ------------------------------------------------------------------

    [Fact]
    public async Task Encash_WhenUseCaseThrows_ShowsErrorMessage_AndDoesNotNotify()
    {
        var spy = new SpySettleOrderBalanceUseCase(_ => Task.FromException<SettleOrderBalanceResult>(
            new PersistenceException("Base de données indisponible.", PersistenceErrorCategory.Unknown)));
        var viewModel = BuildInitializedViewModel(spy);

        var updated = false;
        viewModel.OrderUpdated += (_, _) => updated = true;

        viewModel.EncashBalanceCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.False(updated);
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.StartsWith("Erreur lors de l'encaissement : ", viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Aucun encaissement possible sans use case (erreur de configuration)
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutSettleOrderBalanceUseCase_Throws()
    {
        var orderRepo = new Mock<IOrderRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var advanceUseCase = new Mock<IAdvanceOrderStatusUseCase>();

        Assert.Throws<ArgumentNullException>(() => new OrderDetailViewModel(
            orderRepo.Object, unitOfWork.Object, advanceUseCase.Object, settleOrderBalanceUseCase: null!));
    }

    // ------------------------------------------------------------------
    // (5) Mapping état VM → SettleOrderBalanceCommand (identifiant de commande)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Encash_BuildsCommandFromViewModelState()
    {
        var spy = new SpySettleOrderBalanceUseCase();
        var viewModel = BuildInitializedViewModel(spy);

        viewModel.EncashBalanceCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.NotNull(spy.LastCommand);
        Assert.Equal(42, spy.LastCommand!.OrderId);
    }

    // ------------------------------------------------------------------
    // (6) Garde d'affichage : pas d'encaissement sans solde restant
    // ------------------------------------------------------------------

    [Fact]
    public void EncashBalanceCommand_Disabled_WhenNoRemainingBalance()
    {
        var spy = new SpySettleOrderBalanceUseCase();
        var orderRepo = new Mock<IOrderRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var advanceUseCase = new Mock<IAdvanceOrderStatusUseCase>();

        var viewModel = new OrderDetailViewModel(
            orderRepo.Object, unitOfWork.Object, advanceUseCase.Object, spy);

        var order = new Order
        {
            OrderId = 7,
            OrderNumber = "CMD-PAID",
            Status = OrderStatus.Delivered,
            Sale = new Sale { FinalAmount = 100m, DepositAmount = 100m, RemainingAmount = 0m },
        };
        viewModel.Initialize(order);

        Assert.False(viewModel.HasRemainingBalance);
        Assert.False(viewModel.EncashBalanceCommand.CanExecute(null),
            "la commande d'encaissement est désactivée sans solde restant");
    }
}
