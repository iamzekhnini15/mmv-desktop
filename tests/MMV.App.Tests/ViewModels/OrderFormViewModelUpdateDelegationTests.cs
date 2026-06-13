using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2I — Comportement de présentation de <see cref="OrderFormViewModel"/> après extraction du flux
/// d'<b>édition</b> d'une commande existante vers <see cref="IUpdateOrderUseCase"/>.
///
/// En mode édition, la ViewModel ne met plus à jour ni ne reconstruit/sauvegarde directement la commande : elle
/// mappe son état vers une <see cref="UpdateOrderCommand"/> et <b>délègue</b> au use case. Ces tests vérifient :
/// (1) la délégation effective au use case d'édition (et non à celui de création) + la notification
/// <c>OrderSaved</c> ; (2) la garde anti double-soumission (<c>IsSaving</c>) ; (3) l'affichage d'une erreur
/// propagée par le use case sans notification de succès ; (4) le rejet d'un use case d'édition absent ;
/// (5) le mapping état VM → <see cref="UpdateOrderCommand"/> (id et numéro de la commande éditée, lignes).
///
/// La couverture de persistance (vrai SQLite : champs mis à jour, lignes reconstruites, optique, statut
/// préservé, introuvable) est portée par <c>MMV.Application.Tests.UpdateOrderUseCaseTests</c>.
/// </summary>
public class OrderFormViewModelUpdateDelegationTests
{
    // ------------------------------------------------------------------
    // Doubles de test
    // ------------------------------------------------------------------

    /// <summary>Espion de use case d'édition : compte les appels, mémorise la dernière commande.</summary>
    private sealed class SpyUpdateOrderUseCase : IUpdateOrderUseCase
    {
        private int _executeCount;
        private readonly Func<UpdateOrderCommand, Task<UpdateOrderResult>> _behavior;

        public SpyUpdateOrderUseCase(Func<UpdateOrderCommand, Task<UpdateOrderResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(SuccessResult(cmd)));

        public int ExecuteCount => _executeCount;
        public UpdateOrderCommand? LastCommand { get; private set; }

        public Task<UpdateOrderResult> ExecuteAsync(UpdateOrderCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }

        public static UpdateOrderResult SuccessResult(UpdateOrderCommand command) => new()
        {
            OrderFound = true,
            OrderId = command.OrderId,
            OrderNumber = command.OrderNumber,
            Status = OrderStatus.InProgress,
            EstimatedDelivery = command.EstimatedDelivery
        };
    }

    /// <summary>Espion de use case de création : sert à prouver qu'il n'est PAS appelé en mode édition.</summary>
    private sealed class SpyCreateOrderUseCase : ICreateOrderUseCase
    {
        private int _executeCount;
        public int ExecuteCount => _executeCount;

        public Task<CreateOrderResult> ExecuteAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            return Task.FromResult(new CreateOrderResult { OrderId = command is null ? 0 : 1 });
        }
    }

    private const long CustomerId = 7;
    private const long ProductId = 11;
    private const long OrderId = 555;

    private static readonly Product FrameProduct = new()
    {
        ProductId = ProductId,
        Reference = "MON-001",
        Name = "Monture Test",
        Category = ProductCategoryEnum.MONTURE,
        SalePrice = 20m,
    };

    /// <summary>Construit une commande existante (avec vente/client et une ligne valide) à éditer.</summary>
    private static Order BuildExistingOrder() => new()
    {
        OrderId = OrderId,
        OrderNumber = "CMD-000100",
        Notes = "Notes initiales",
        EstimatedDelivery = new DateTime(2026, 1, 1),
        Status = OrderStatus.InProgress,
        SaleId = 1,
        Sale = new Sale { SaleId = 1, CustomerId = CustomerId },
        OrderItems = new List<OrderItem>
        {
            new() { OrderItemId = 1, ProductId = ProductId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 20m }
        }
    };

    /// <summary>
    /// Construit une ViewModel en mode <b>édition</b>, initialisée (clients/produits chargés, commande existante
    /// rechargée → client sélectionné + ligne valide) — donc prête à être sauvegardée.
    /// </summary>
    private static async Task<OrderFormViewModel> BuildReadyToEditViewModelAsync(
        IUpdateOrderUseCase updateUseCase, ICreateOrderUseCase? createUseCase = null)
    {
        var customerRepo = new Mock<ICustomerRepository>();
        var productRepo = new Mock<IProductRepository>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();
        var numberSequence = new Mock<INumberSequenceService>();

        customerRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Customer> { new() { CustomerId = CustomerId, FirstName = "Cli", LastName = "Ent" } });
        productRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Product> { FrameProduct });
        prescriptionRepo.Setup(r => r.GetByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());

        var viewModel = new OrderFormViewModel(
            customerRepo.Object, productRepo.Object, prescriptionRepo.Object,
            numberSequence.Object, createUseCase ?? new SpyCreateOrderUseCase(), updateUseCase,
            BuildExistingOrder());

        await viewModel.InitializeAsync();
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
    // (0) Pré-condition : la VM d'édition est bien prête à sauvegarder
    // ------------------------------------------------------------------

    [Fact]
    public async Task EditMode_AfterInitialize_IsReadyToSave()
    {
        var viewModel = await BuildReadyToEditViewModelAsync(new SpyUpdateOrderUseCase());

        Assert.True(viewModel.IsEditMode);
        Assert.NotNull(viewModel.SelectedCustomer);
        Assert.Single(viewModel.OrderItems);
        Assert.True(viewModel.SaveCommand.CanExecute(null), "client + ligne valide rechargés → sauvegarde possible");
    }

    // ------------------------------------------------------------------
    // (1) L'édition délègue au use case d'édition (pas de création) et notifie OrderSaved
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_Edit_DelegatesToUpdateUseCase_NotCreate_AndRaisesOrderSaved()
    {
        var updateSpy = new SpyUpdateOrderUseCase();
        var createSpy = new SpyCreateOrderUseCase();
        var viewModel = await BuildReadyToEditViewModelAsync(updateSpy, createSpy);

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, updateSpy.ExecuteCount);
        Assert.Equal(0, createSpy.ExecuteCount);
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
        var updateSpy = new SpyUpdateOrderUseCase(async cmd =>
        {
            await gate.Task;
            return SpyUpdateOrderUseCase.SuccessResult(cmd);
        });

        var viewModel = await BuildReadyToEditViewModelAsync(updateSpy);

        var savedCount = 0;
        viewModel.OrderSaved += (_, _) => Interlocked.Increment(ref savedCount);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsSaving);
        Assert.Equal(1, updateSpy.ExecuteCount);

        // 2e clic pendant la sauvegarde : ignoré par la garde (aucun nouvel appel).
        viewModel.SaveCommand.Execute(null);
        Assert.Equal(1, updateSpy.ExecuteCount);

        gate.SetResult();
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, updateSpy.ExecuteCount);
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
        var updateSpy = new SpyUpdateOrderUseCase(_ => Task.FromException<UpdateOrderResult>(
            new PersistenceException(controlledMessage, PersistenceErrorCategory.UniqueConstraint)));

        var viewModel = await BuildReadyToEditViewModelAsync(updateSpy);

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, updateSpy.ExecuteCount);
        Assert.False(saved, "aucune notification de succès en cas d'échec");
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Contains(controlledMessage, viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Aucune sauvegarde possible sans use case d'édition (erreur de configuration)
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutUpdateOrderUseCase_Throws()
    {
        var customerRepo = new Mock<ICustomerRepository>();
        var productRepo = new Mock<IProductRepository>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();
        var numberSequence = new Mock<INumberSequenceService>();
        var createOrderUseCase = new Mock<ICreateOrderUseCase>();

        Assert.Throws<ArgumentNullException>(() => new OrderFormViewModel(
            customerRepo.Object, productRepo.Object, prescriptionRepo.Object,
            numberSequence.Object, createOrderUseCase.Object, updateOrderUseCase: null!));
    }

    // ------------------------------------------------------------------
    // (5) Mapping état VM → UpdateOrderCommand (id et numéro de la commande éditée, lignes)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_BuildsUpdateCommandFromFormState()
    {
        var updateSpy = new SpyUpdateOrderUseCase();
        var viewModel = await BuildReadyToEditViewModelAsync(updateSpy);
        viewModel.Notes = "Notes modifiées";

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.NotNull(updateSpy.LastCommand);
        var command = updateSpy.LastCommand!;
        Assert.Equal(OrderId, command.OrderId);          // identifiant de la commande éditée
        Assert.Equal("CMD-000100", command.OrderNumber); // numéro existant (lecture seule en édition)
        Assert.Equal("Notes modifiées", command.Notes);
        Assert.Single(command.Lines);
        Assert.Equal(ProductId, command.Lines[0].ProductId);
        Assert.Equal(OrderItemType.Frame, command.Lines[0].ItemType);
        Assert.Equal(1, command.Lines[0].Quantity);
        Assert.Equal(20m, command.Lines[0].UnitPrice);
    }
}
