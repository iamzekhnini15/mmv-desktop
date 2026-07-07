using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.GetOrderDetails;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2E — Comportement de présentation de <see cref="OrderDetailViewModel"/> après extraction du flux
/// d'<b>avancement de statut</b> vers <see cref="IAdvanceOrderStatusUseCase"/>.
///
/// La VM ne met plus directement à jour le statut, ne crée plus les mouvements de stock ni la notification :
/// elle mappe son état (statut courant, transition résolue, libellés d'affichage) vers une
/// <see cref="AdvanceOrderStatusCommand"/> et <b>délègue</b>. Ces tests vérifient : (1) la délégation effective
/// + l'événement <c>OrderUpdated</c> ; (2) le message « Commande introuvable. » sans mise à jour d'état ;
/// (3) l'affichage d'une erreur propagée par le use case ; (4) le rejet d'un use case absent ; (5) le mapping
/// état VM → commande (dont les libellés d'affichage français préservés).
///
/// La couverture de persistance (vrai SQLite : statut, mouvements de stock, décrément, notification) est portée
/// par <c>MMV.Application.Tests.AdvanceOrderStatusUseCaseTests</c>.
/// </summary>
public class OrderDetailViewModelAdvanceDelegationTests
{
    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpyAdvanceOrderStatusUseCase : IAdvanceOrderStatusUseCase
    {
        private int _executeCount;
        private readonly Func<AdvanceOrderStatusCommand, Task<AdvanceOrderStatusResult>> _behavior;

        public SpyAdvanceOrderStatusUseCase(Func<AdvanceOrderStatusCommand, Task<AdvanceOrderStatusResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(SuccessResult(cmd)));

        public int ExecuteCount => _executeCount;
        public AdvanceOrderStatusCommand? LastCommand { get; private set; }

        public Task<AdvanceOrderStatusResult> ExecuteAsync(AdvanceOrderStatusCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }

        public static AdvanceOrderStatusResult SuccessResult(AdvanceOrderStatusCommand cmd) => new()
        {
            OrderFound = true,
            Order = new Order { OrderId = cmd.OrderId, OrderNumber = "CMD-FRESH", Status = cmd.NextStatus },
            OldStatus = cmd.CurrentStatus,
            NewStatus = cmd.NextStatus,
            HasNotification = true,
        };
    }

    /// <summary>Construit une VM initialisée sur une commande « Nouveau » prête à avancer (CanAdvanceStatus).</summary>
    private static OrderDetailViewModel BuildInitializedViewModel(IAdvanceOrderStatusUseCase useCase)
    {
        var settleUseCase = new Mock<ISettleOrderBalanceUseCase>();

        var viewModel = new OrderDetailViewModel(useCase, settleUseCase.Object);

        var order = new Order
        {
            OrderId = 42,
            OrderNumber = "CMD-000500",
            Status = OrderStatus.New,
            Sale = new Sale { Customer = new Customer { FirstName = "Jean", LastName = "Dupont" } },
        };
        order.OrderItems.Add(new OrderItem { ProductId = 1, ItemType = OrderItemType.LensOd, Quantity = 1 });

        viewModel.Initialize(GetOrderDetailsUseCase.MapToDto(order));
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
    // (1) L'avancement délègue au use case et notifie OrderUpdated
    // ------------------------------------------------------------------

    [Fact]
    public async Task AdvanceStatus_DelegatesToUseCase_AndRaisesOrderUpdated()
    {
        var spy = new SpyAdvanceOrderStatusUseCase();
        var viewModel = BuildInitializedViewModel(spy);

        OrderDetailsDto? updatedWith = null;
        viewModel.OrderUpdated += (_, order) => updatedWith = order;

        viewModel.AdvanceStatusCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.NotNull(updatedWith);
        Assert.Equal(OrderStatus.ToFabricate, viewModel.Order!.Status);
        Assert.Null(viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (2) Commande introuvable : message dédié, aucune mise à jour d'état
    // ------------------------------------------------------------------

    [Fact]
    public async Task AdvanceStatus_WhenOrderNotFound_ShowsNotFoundMessage_AndDoesNotUpdate()
    {
        var spy = new SpyAdvanceOrderStatusUseCase(_ => Task.FromResult(new AdvanceOrderStatusResult { OrderFound = false }));
        var viewModel = BuildInitializedViewModel(spy);

        var updated = false;
        viewModel.OrderUpdated += (_, _) => updated = true;

        viewModel.AdvanceStatusCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal("Commande introuvable.", viewModel.ErrorMessage);
        Assert.False(updated, "aucune mise à jour d'état si la commande est introuvable");
        Assert.Equal(OrderStatus.New, viewModel.Order!.Status);
    }

    // ------------------------------------------------------------------
    // (3) Exception propagée par le use case → message d'erreur affiché
    // ------------------------------------------------------------------

    [Fact]
    public async Task AdvanceStatus_WhenUseCaseThrows_ShowsErrorMessage_AndDoesNotNotify()
    {
        var spy = new SpyAdvanceOrderStatusUseCase(_ => Task.FromException<AdvanceOrderStatusResult>(
            new PersistenceException("Base de données indisponible.", PersistenceErrorCategory.Unknown)));
        var viewModel = BuildInitializedViewModel(spy);

        var updated = false;
        viewModel.OrderUpdated += (_, _) => updated = true;

        viewModel.AdvanceStatusCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.False(updated);
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.StartsWith("Erreur lors du changement de statut : ", viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Aucun avancement possible sans use case (erreur de configuration)
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutAdvanceOrderStatusUseCase_Throws()
    {
        var settleUseCase = new Mock<ISettleOrderBalanceUseCase>();

        Assert.Throws<ArgumentNullException>(() => new OrderDetailViewModel(
            advanceOrderStatusUseCase: null!, settleUseCase.Object));
    }

    // ------------------------------------------------------------------
    // (5) Mapping état VM → AdvanceOrderStatusCommand (libellés d'affichage préservés)
    // ------------------------------------------------------------------

    [Fact]
    public async Task AdvanceStatus_BuildsCommandFromViewModelState()
    {
        var spy = new SpyAdvanceOrderStatusUseCase();
        var viewModel = BuildInitializedViewModel(spy);

        viewModel.AdvanceStatusCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsLoading);

        Assert.NotNull(spy.LastCommand);
        var command = spy.LastCommand!;
        Assert.Equal(42, command.OrderId);
        Assert.Equal(OrderStatus.New, command.CurrentStatus);
        Assert.Equal(OrderStatus.ToFabricate, command.NextStatus);
        Assert.Equal("Jean Dupont", command.CustomerDisplayName);
        Assert.Equal("Nouveau", command.CurrentStatusDisplay);
        Assert.Equal("À fabriquer", command.NextStatusDisplay);
    }
}
