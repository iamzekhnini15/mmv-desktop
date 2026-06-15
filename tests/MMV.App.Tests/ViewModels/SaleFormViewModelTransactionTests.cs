using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2C — Comportement de présentation de <see cref="SaleFormViewModel"/> après extraction de
/// l'orchestration métier vers <see cref="IRegisterSaleUseCase"/>.
///
/// La ViewModel ne contient plus de transaction, de numérotation, de décrément de stock ni d'orchestration
/// de repositories pour le flux de vente : elle <b>construit une commande</b> et <b>délègue</b> au use case.
/// Ces tests vérifient donc : (1) la délégation effective (un appel au use case) et la notification
/// <c>OrderSaved</c> ; (2) la garde d'entrée panier vide ; (3) la garde anti double-soumission
/// (<c>IsSaving</c>) ; (4)(5) l'affichage des erreurs typées P2A propagées par le use case
/// (<see cref="InsufficientStockException"/>, <see cref="PersistenceException"/>) ; (6) le rejet d'un use
/// case absent ; (7) le mapping état VM → <see cref="RegisterSaleCommand"/> ; (8) le chemin de production
/// <c>CustomerDetailViewModel → SaleFormViewModel</c> qui transmet le use case.
///
/// La couverture transactionnelle de bout en bout (vrai SQLite, rollback, numéro non consommé) a été
/// <b>déplacée</b> au niveau du use case (<c>MMV.Application.Tests.RegisterSaleUseCaseTests</c>).
/// </summary>
public class SaleFormViewModelTransactionTests
{
    // ------------------------------------------------------------------
    // Doubles de test
    // ------------------------------------------------------------------

    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpyRegisterSaleUseCase : IRegisterSaleUseCase
    {
        private int _executeCount;
        private readonly Func<RegisterSaleCommand, Task<RegisterSaleResult>> _behavior;

        public SpyRegisterSaleUseCase(Func<RegisterSaleCommand, Task<RegisterSaleResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(SuccessResult(cmd)));

        public int ExecuteCount => _executeCount;
        public RegisterSaleCommand? LastCommand { get; private set; }

        public Task<RegisterSaleResult> ExecuteAsync(RegisterSaleCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }

        public static RegisterSaleResult SuccessResult(RegisterSaleCommand command) => new()
        {
            SaleId = 1,
            SaleNumber = "VTE-000001",
            Status = command.IsCounterSale ? SaleStatus.Delivered : SaleStatus.AwaitingLenses,
            FinalAmount = command.FinalAmount,
            RemainingAmount = command.RemainingAmount,
            Sale = new Sale { SaleId = 1, SaleNumber = "VTE-000001" }
        };
    }

    private static SaleFormViewModel BuildViewModel(IRegisterSaleUseCase useCase)
        => new(productRepository: null, prescriptionRepository: null, useCase);

    private static OrderItem FrameItem() => new()
    {
        ProductId = 1,
        ItemType = OrderItemType.Frame,
        Quantity = 1,
        UnitPrice = 10m,
    };

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
    // (1) La sauvegarde délègue au use case et notifie OrderSaved
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_DelegatesToUseCase_AndRaisesOrderSaved()
    {
        var spy = new SpyRegisterSaleUseCase();
        var viewModel = BuildViewModel(spy);
        viewModel.AddOrderItem(FrameItem());

        Sale? saved = null;
        viewModel.OrderSaved += (_, s) => saved = s;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.NotNull(saved);
        Assert.Null(viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (2) Panier vide : garde d'entrée UI, aucun appel au use case
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_EmptyCart_ShowsError_AndDoesNotCallUseCase()
    {
        var spy = new SpyRegisterSaleUseCase();
        var viewModel = BuildViewModel(spy);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(0, spy.ExecuteCount);
        Assert.Equal("Ajoutez au moins un article au panier", viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (3) Double soumission empêchée par la garde IsSaving
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_WhileSaving_SecondInvocationIsIgnored()
    {
        // Le use case bloque jusqu'à libération du verrou, simulant une sauvegarde en cours.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spy = new SpyRegisterSaleUseCase(async cmd =>
        {
            await gate.Task;
            return SpyRegisterSaleUseCase.SuccessResult(cmd);
        });

        var viewModel = BuildViewModel(spy);
        viewModel.AddOrderItem(FrameItem());

        var savedCount = 0;
        viewModel.OrderSaved += (_, _) => Interlocked.Increment(ref savedCount);

        // 1er clic : la sauvegarde démarre et se bloque dans le use case.
        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsSaving);
        Assert.Equal(1, spy.ExecuteCount);

        // 2e clic pendant la sauvegarde : ignoré par la garde (aucun nouvel appel).
        viewModel.SaveCommand.Execute(null);
        Assert.Equal(1, spy.ExecuteCount);

        // Libération : la 1re sauvegarde se termine normalement.
        gate.SetResult();
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(1, savedCount);
    }

    // ------------------------------------------------------------------
    // (4) InsufficientStockException propagée → message contrôlé, pas de notification
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_WhenInsufficientStock_ShowsControlledMessage_AndDoesNotNotifySaved()
    {
        var insufficient = new InsufficientStockException(productId: 1, requestedQuantity: 1, availableQuantity: 0);
        var spy = new SpyRegisterSaleUseCase(_ => Task.FromException<RegisterSaleResult>(insufficient));

        var viewModel = BuildViewModel(spy);
        viewModel.AddOrderItem(FrameItem());

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(insufficient.Message, viewModel.ErrorMessage);
        Assert.False(saved, "aucune notification de succès quand le stock est insuffisant");
    }

    // ------------------------------------------------------------------
    // (5) PersistenceException propagée → message utilisateur assaini
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_WhenPersistenceExceptionThrown_ShowsControlledMessage()
    {
        const string controlledMessage =
            "Un enregistrement avec ces informations existe déjà (doublon). Aucune modification n'a été conservée.";
        var spy = new SpyRegisterSaleUseCase(_ => Task.FromException<RegisterSaleResult>(
            new PersistenceException(controlledMessage, PersistenceErrorCategory.UniqueConstraint)));

        var viewModel = BuildViewModel(spy);
        viewModel.AddOrderItem(FrameItem());

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(controlledMessage, viewModel.ErrorMessage);
        Assert.False(saved, "aucune notification de succès en cas d'échec de persistance");
    }

    // ------------------------------------------------------------------
    // (6) Aucune sauvegarde possible sans use case (erreur de configuration)
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRegisterSaleUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SaleFormViewModel(
            productRepository: null, prescriptionRepository: null, registerSaleUseCase: null!));
    }

    // ------------------------------------------------------------------
    // (7) Mapping état VM → RegisterSaleCommand (validation/mapping de présentation)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_BuildsCommandFromCartState()
    {
        var spy = new SpyRegisterSaleUseCase();
        var viewModel = BuildViewModel(spy);
        viewModel.CustomerId = 42;
        viewModel.IsCounterSale = true;
        viewModel.SelectedPaymentMethod = "Carte Bancaire";
        viewModel.AddOrderItem(FrameItem());

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.NotNull(spy.LastCommand);
        var command = spy.LastCommand!;
        Assert.Equal(42, command.CustomerId);
        Assert.True(command.IsCounterSale);
        Assert.Equal(PaymentMethod.Card, command.PaymentMethod);
        Assert.Equal(10m, command.FinalAmount); // 1 x 10, sans remise
        Assert.Single(command.Lines);
        Assert.Equal(OrderItemType.Frame, command.Lines[0].ItemType);
        Assert.Equal(1, command.Lines[0].Quantity);
        Assert.Equal(10m, command.Lines[0].UnitPrice);
    }

    // ------------------------------------------------------------------
    // (8) Chemin de production : le use case est transmis jusqu'au SaleFormViewModel
    // ------------------------------------------------------------------

    [Fact]
    public async Task ProductionChain_CustomerDetail_TransmitsUseCaseToSaleForm()
    {
        var customerRepo = new Mock<ICustomerRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();
        var productRepo = new Mock<IProductRepository>();
        var saleRepo = new Mock<ISaleRepository>();

        prescriptionRepo.Setup(r => r.GetLatestByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);
        prescriptionRepo.Setup(r => r.GetByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        productRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Product>());
        saleRepo.Setup(r => r.GetByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sale>());

        var spy = new SpyRegisterSaleUseCase();

        // Reproduit la construction de production : CustomerDetailViewModel reçoit le use case par DI (via
        // CustomersViewModel) et DOIT le transmettre à SaleFormViewModel.
        var detail = new CustomerDetailViewModel(
            customerRepo.Object, unitOfWork.Object, prescriptionRepo.Object,
            productRepo.Object, saleRepo.Object, spy,
            Mock.Of<ICreatePrescriptionUseCase>(), Mock.Of<IUpdatePrescriptionUseCase>(),
            Mock.Of<IDeletePrescriptionUseCase>());

        await detail.InitializeAsync(new Customer { CustomerId = 7, FirstName = "Prod", LastName = "Chain" });

        Assert.NotNull(detail.SaleFormViewModel);

        detail.SaleFormViewModel!.AddOrderItem(FrameItem());
        detail.SaleFormViewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !detail.SaleFormViewModel.IsSaving);

        // Le use case injecté au CustomerDetailViewModel a bien été transmis au SaleFormViewModel et utilisé.
        Assert.Equal(1, spy.ExecuteCount);
    }
}
