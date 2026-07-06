using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.ListCustomersForPicker;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Products.ListProductsForOrderPicker;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2D — Comportement de présentation de <see cref="OrderFormViewModel"/> après extraction du flux de
/// <b>création</b> d'une commande fournisseur vers <see cref="ICreateOrderUseCase"/>.
///
/// La branche création de la ViewModel ne construit/sauvegarde plus directement la commande : elle mappe son
/// état vers une <see cref="CreateOrderCommand"/> et <b>délègue</b> au use case. Ces tests vérifient :
/// (1) la délégation effective + la notification <c>OrderSaved</c> ; (2) la garde anti double-soumission
/// (<c>IsSaving</c>) ; (3) l'affichage d'une erreur propagée par le use case sans notification de succès ;
/// (4) la garde d'entrée (sans article valide, aucun appel) ; (5) le rejet d'un use case absent ;
/// (6) le mapping état VM → <see cref="CreateOrderCommand"/> (dont le numéro <c>ORDER</c> attribué à
/// l'ouverture, préservé).
///
/// La couverture de persistance (vrai SQLite : numéro préservé, statut, articles, optique) est portée par
/// <c>MMV.Application.Tests.CreateOrderUseCaseTests</c>.
/// </summary>
public class OrderFormViewModelCreateDelegationTests
{
    // ------------------------------------------------------------------
    // Doubles de test
    // ------------------------------------------------------------------

    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpyCreateOrderUseCase : ICreateOrderUseCase
    {
        private int _executeCount;
        private readonly Func<CreateOrderCommand, Task<CreateOrderResult>> _behavior;

        public SpyCreateOrderUseCase(Func<CreateOrderCommand, Task<CreateOrderResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(SuccessResult(cmd)));

        public int ExecuteCount => _executeCount;
        public CreateOrderCommand? LastCommand { get; private set; }

        public Task<CreateOrderResult> ExecuteAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }

        public static CreateOrderResult SuccessResult(CreateOrderCommand command) => new()
        {
            OrderId = 1,
            OrderNumber = command.OrderNumber,
            Status = OrderStatus.New,
            EstimatedDelivery = command.EstimatedDelivery
        };
    }

    private static readonly OrderProductPickerDto FrameProduct = new()
    {
        ProductId = 1,
        Reference = "MON-001",
        Name = "Monture Test",
        Category = ProductCategoryEnum.MONTURE,
        SalePrice = 20m,
    };

    /// <summary>
    /// Construit une ViewModel en mode création, initialisée (clients/produits/numéro chargés), avec un client
    /// sélectionné et une ligne d'article valide — donc prête à être sauvegardée.
    /// </summary>
    private static async Task<OrderFormViewModel> BuildReadyToSaveViewModelAsync(ICreateOrderUseCase useCase)
    {
        var customersUseCase = new Mock<IListCustomersForPickerUseCase>();
        var productsUseCase = new Mock<IListProductsForOrderPickerUseCase>();
        var prescriptionsUseCase = new Mock<IListPrescriptionsByCustomerUseCase>();
        var numberSequence = new Mock<INumberSequenceService>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();

        customersUseCase.Setup(u => u.ExecuteAsync(It.IsAny<ListCustomersForPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CustomerPickerItemDto> { new() { CustomerId = 7, FirstName = "Cli", LastName = "Ent" } });
        productsUseCase.Setup(u => u.ExecuteAsync(It.IsAny<ListProductsForOrderPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderProductPickerDto> { FrameProduct });
        prescriptionsUseCase.Setup(u => u.ExecuteAsync(It.IsAny<ListPrescriptionsByCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PrescriptionListItemDto>());
        numberSequence.Setup(s => s.NextNumberAsync(DocumentSequenceNames.Order, It.IsAny<CancellationToken>()))
            .ReturnsAsync("CMD-000001");

        var viewModel = new OrderFormViewModel(
            customersUseCase.Object, productsUseCase.Object, prescriptionsUseCase.Object,
            numberSequence.Object, useCase, updateOrderUseCase.Object);

        await viewModel.InitializeAsync();

        viewModel.SelectedCustomer = new CustomerPickerItemDto { CustomerId = 7, FirstName = "Cli", LastName = "Ent" };
        viewModel.AddFrameCommand.Execute(null);
        viewModel.OrderItems[0].SelectedProduct = FrameProduct; // fixe UnitPrice = SalePrice (20) → ligne valide

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
    // (1) La création délègue au use case et notifie OrderSaved
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_Create_DelegatesToUseCase_AndRaisesOrderSaved()
    {
        var spy = new SpyCreateOrderUseCase();
        var viewModel = await BuildReadyToSaveViewModelAsync(spy);

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.True(saved);
        Assert.Null(viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (2) Double soumission empêchée par la garde IsSaving
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhileSaving_SecondInvocationIsIgnored()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spy = new SpyCreateOrderUseCase(async cmd =>
        {
            await gate.Task;
            return SpyCreateOrderUseCase.SuccessResult(cmd);
        });

        var viewModel = await BuildReadyToSaveViewModelAsync(spy);

        var savedCount = 0;
        viewModel.OrderSaved += (_, _) => Interlocked.Increment(ref savedCount);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsSaving);
        Assert.Equal(1, spy.ExecuteCount);

        // 2e clic pendant la sauvegarde : ignoré par la garde (aucun nouvel appel).
        viewModel.SaveCommand.Execute(null);
        Assert.Equal(1, spy.ExecuteCount);

        gate.SetResult();
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(1, savedCount);
    }

    // ------------------------------------------------------------------
    // (3) Exception propagée par le use case → message affiché, pas de notification
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenUseCaseThrows_ShowsErrorMessage_AndDoesNotNotifySaved()
    {
        const string controlledMessage =
            "Un enregistrement avec ces informations existe déjà (doublon). Aucune modification n'a été conservée.";
        var spy = new SpyCreateOrderUseCase(_ => Task.FromException<CreateOrderResult>(
            new PersistenceException(controlledMessage, PersistenceErrorCategory.UniqueConstraint)));

        var viewModel = await BuildReadyToSaveViewModelAsync(spy);

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.False(saved, "aucune notification de succès en cas d'échec");
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Contains(controlledMessage, viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Garde d'entrée : sans article valide, aucun appel au use case
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WithoutValidItems_DoesNotCallUseCase()
    {
        var spy = new SpyCreateOrderUseCase();

        var customersUseCase = new Mock<IListCustomersForPickerUseCase>();
        var productsUseCase = new Mock<IListProductsForOrderPickerUseCase>();
        var prescriptionsUseCase = new Mock<IListPrescriptionsByCustomerUseCase>();
        var numberSequence = new Mock<INumberSequenceService>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();
        customersUseCase.Setup(u => u.ExecuteAsync(It.IsAny<ListCustomersForPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CustomerPickerItemDto>());
        productsUseCase.Setup(u => u.ExecuteAsync(It.IsAny<ListProductsForOrderPickerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrderProductPickerDto>());
        numberSequence.Setup(s => s.NextNumberAsync(DocumentSequenceNames.Order, It.IsAny<CancellationToken>()))
            .ReturnsAsync("CMD-000001");

        var viewModel = new OrderFormViewModel(
            customersUseCase.Object, productsUseCase.Object, prescriptionsUseCase.Object,
            numberSequence.Object, spy, updateOrderUseCase.Object);
        await viewModel.InitializeAsync();

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(0, spy.ExecuteCount);
    }

    // ------------------------------------------------------------------
    // (5) Aucune sauvegarde possible sans use case (erreur de configuration)
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutCreateOrderUseCase_Throws()
    {
        var customersUseCase = new Mock<IListCustomersForPickerUseCase>();
        var productsUseCase = new Mock<IListProductsForOrderPickerUseCase>();
        var prescriptionsUseCase = new Mock<IListPrescriptionsByCustomerUseCase>();
        var numberSequence = new Mock<INumberSequenceService>();
        var updateOrderUseCase = new Mock<IUpdateOrderUseCase>();

        Assert.Throws<ArgumentNullException>(() => new OrderFormViewModel(
            customersUseCase.Object, productsUseCase.Object, prescriptionsUseCase.Object,
            numberSequence.Object, createOrderUseCase: null!, updateOrderUseCase.Object));
    }

    // ------------------------------------------------------------------
    // (6) Mapping état VM → CreateOrderCommand (dont le numéro ORDER préservé)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_BuildsCommandFromFormState()
    {
        var spy = new SpyCreateOrderUseCase();
        var viewModel = await BuildReadyToSaveViewModelAsync(spy);
        viewModel.Notes = "Commande test";

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.NotNull(spy.LastCommand);
        var command = spy.LastCommand!;
        Assert.Equal("CMD-000001", command.OrderNumber); // numéro attribué à l'ouverture, préservé
        Assert.Equal("Commande test", command.Notes);
        Assert.Single(command.Lines);
        Assert.Equal(1, command.Lines[0].ProductId);
        Assert.Equal(OrderItemType.Frame, command.Lines[0].ItemType);
        Assert.Equal(1, command.Lines[0].Quantity);
        Assert.Equal(20m, command.Lines[0].UnitPrice);
    }
}
