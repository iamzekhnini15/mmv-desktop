using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2A-1C / P2A-1C-R2 — Comportement transactionnel et anti double-soumission de
/// <see cref="SaleFormViewModel.ExecuteSave"/> (flux d'écriture multi-étapes prioritaire).
///
/// Vérifie que la sauvegarde : (1) passe TOUJOURS par la frontière transactionnelle
/// (<see cref="ITransactionRunner"/>) ; (2) est <b>impossible à construire</b> sans runner — il n'existe
/// plus de repli non transactionnel (R2) ; (3) est protégée contre la double soumission ; (4) expose un
/// message utilisateur contrôlé quand une <see cref="PersistenceException"/> remonte ; (5) reçoit bien le
/// runner par le chemin de production <c>CustomerDetailViewModel → SaleFormViewModel</c>.
///
/// La preuve « aucune écriture partielle / rollback complet » sur un vrai SQLite est apportée par
/// <c>EfTransactionRunnerTests</c> (couche Infrastructure) ; ici on isole la logique du ViewModel.
/// </summary>
public class SaleFormViewModelTransactionTests
{
    // ------------------------------------------------------------------
    // Doubles de test
    // ------------------------------------------------------------------

    /// <summary>Exécute l'opération telle quelle (frontière transactionnelle simulée), en comptant les appels.</summary>
    private sealed class PassThroughTransactionRunner : ITransactionRunner
    {
        public int RunCount { get; private set; }

        public Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            RunCount++;
            return operation(cancellationToken);
        }

        public Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
        {
            RunCount++;
            return operation(cancellationToken);
        }
    }

    /// <summary>Échoue toujours avec l'exception fournie (faulted task), sans exécuter l'opération.</summary>
    private sealed class FailingTransactionRunner : ITransactionRunner
    {
        private readonly Exception _toThrow;

        public FailingTransactionRunner(Exception toThrow) => _toThrow = toThrow;

        public Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => Task.FromException(_toThrow);

        public Task<TResult> RunAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
            => Task.FromException<TResult>(_toThrow);
    }

    private sealed class Mocks
    {
        public Mock<ISaleRepository> Sale { get; } = new();
        public Mock<IOrderRepository> Order { get; } = new();
        public Mock<IProductRepository> Product { get; } = new();
        public Mock<IPrescriptionRepository> Prescription { get; } = new();
        public Mock<IStockMovementRepository> Stock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IStockMutationService> StockMutation { get; } = new();

        public SaleFormViewModel Build(ITransactionRunner runner) => new(
            Sale.Object, Order.Object, Product.Object, Prescription.Object, Stock.Object, UnitOfWork.Object,
            runner, StockMutation.Object);
    }

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
    // (R2) Aucune persistance possible sans frontière transactionnelle
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutTransactionRunner_Throws_NoMultiWriteWithoutTransaction()
    {
        var mocks = new Mocks();

        // L'absence de runner est une erreur de configuration : le ViewModel ne peut pas être construit.
        Assert.Throws<ArgumentNullException>(() => new SaleFormViewModel(
            mocks.Sale.Object, mocks.Order.Object, mocks.Product.Object, mocks.Prescription.Object,
            mocks.Stock.Object, mocks.UnitOfWork.Object, null!, mocks.StockMutation.Object));

        // Donc : aucune vente créée, aucun SaveChanges appelé — pas de chemin non transactionnel.
        mocks.Sale.Verify(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Never);
        mocks.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // (R2-bis / P2A-1D) Aucune sortie de stock possible sans décrément sûr
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutStockMutationService_Throws_NoUnsafeStockDecrement()
    {
        var mocks = new Mocks();

        // L'absence du service de décrément sûr est une erreur de configuration : construction rejetée.
        Assert.Throws<ArgumentNullException>(() => new SaleFormViewModel(
            mocks.Sale.Object, mocks.Order.Object, mocks.Product.Object, mocks.Prescription.Object,
            mocks.Stock.Object, mocks.UnitOfWork.Object, new PassThroughTransactionRunner(), null!));
    }

    // ------------------------------------------------------------------
    // (1) La sauvegarde passe par la frontière transactionnelle
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_RoutesMultiWriteThroughTransactionRunner()
    {
        var mocks = new Mocks();
        mocks.Sale.Setup(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sale s, CancellationToken _) => s);
        mocks.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var runner = new PassThroughTransactionRunner();
        var viewModel = mocks.Build(runner);
        viewModel.AddOrderItem(FrameItem());

        Sale? saved = null;
        viewModel.OrderSaved += (_, s) => saved = s;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, runner.RunCount);
        mocks.Sale.Verify(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(saved);
        Assert.Null(viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (2) Double soumission empêchée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_WhileSaving_SecondInvocationIsIgnored()
    {
        var mocks = new Mocks();
        mocks.Sale.Setup(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sale s, CancellationToken _) => s);

        // Le 1er SaveChanges bloque jusqu'à libération du verrou ; les suivants renvoient immédiatement.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveCalls = 0;
        mocks.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (Interlocked.Increment(ref saveCalls) == 1)
                {
                    await gate.Task;
                }

                return 1;
            });

        var viewModel = mocks.Build(new PassThroughTransactionRunner());
        viewModel.AddOrderItem(FrameItem());

        var savedCount = 0;
        viewModel.OrderSaved += (_, _) => Interlocked.Increment(ref savedCount);

        // 1er clic : la sauvegarde démarre et se bloque sur le 1er SaveChanges.
        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsSaving);
        mocks.Sale.Verify(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Once);

        // 2e clic pendant la sauvegarde : doit être ignoré par la garde (aucune nouvelle vente créée).
        viewModel.SaveCommand.Execute(null);
        mocks.Sale.Verify(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Once);

        // Libération : la 1re sauvegarde se termine normalement.
        gate.SetResult();
        await WaitUntilAsync(() => !viewModel.IsSaving);

        mocks.Sale.Verify(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, savedCount);
    }

    // ------------------------------------------------------------------
    // (3) Erreur de persistance contrôlée → message utilisateur assaini
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_WhenPersistenceExceptionThrown_ShowsControlledMessage()
    {
        var mocks = new Mocks();
        const string controlledMessage =
            "Un enregistrement avec ces informations existe déjà (doublon). Aucune modification n'a été conservée.";
        var runner = new FailingTransactionRunner(
            new PersistenceException(controlledMessage, PersistenceErrorCategory.UniqueConstraint));

        var viewModel = mocks.Build(runner);
        viewModel.AddOrderItem(FrameItem());

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(controlledMessage, viewModel.ErrorMessage);
        Assert.False(saved, "aucune notification de succès en cas d'échec de persistance");
    }

    // ------------------------------------------------------------------
    // (5) Chemin de production : le runner est transmis jusqu'au SaleFormViewModel
    // ------------------------------------------------------------------

    [Fact]
    public async Task ProductionChain_CustomerDetail_TransmitsRunnerToSaleForm()
    {
        var customerRepo = new Mock<ICustomerRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var orderRepo = new Mock<IOrderRepository>();
        var prescriptionRepo = new Mock<IPrescriptionRepository>();
        var productRepo = new Mock<IProductRepository>();
        var saleRepo = new Mock<ISaleRepository>();
        var stockRepo = new Mock<IStockMovementRepository>();

        prescriptionRepo.Setup(r => r.GetLatestByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);
        prescriptionRepo.Setup(r => r.GetByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Prescription>());
        productRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Product>());
        saleRepo.Setup(r => r.GetByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sale>());
        saleRepo.Setup(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sale s, CancellationToken _) => s);
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var spyRunner = new PassThroughTransactionRunner();
        var stockMutation = new Mock<IStockMutationService>();

        // Reproduit la construction de production : CustomerDetailViewModel reçoit le runner et le service
        // de décrément par DI (via CustomersViewModel) et DOIT les transmettre à SaleFormViewModel.
        var detail = new CustomerDetailViewModel(
            customerRepo.Object, unitOfWork.Object, orderRepo.Object, prescriptionRepo.Object,
            productRepo.Object, saleRepo.Object, stockRepo.Object, spyRunner, stockMutation.Object);

        await detail.InitializeAsync(new Customer { CustomerId = 7, FirstName = "Prod", LastName = "Chain" });

        Assert.NotNull(detail.SaleFormViewModel);

        detail.SaleFormViewModel!.AddOrderItem(FrameItem());
        detail.SaleFormViewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !detail.SaleFormViewModel.IsSaving);

        // Le runner injecté au CustomerDetailViewModel a bien été transmis au SaleFormViewModel et utilisé.
        Assert.Equal(1, spyRunner.RunCount);
        saleRepo.Verify(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ------------------------------------------------------------------
    // (P2A-1D / 6) Vente comptoir : la sortie de stock passe par le décrément atomique
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_CounterSale_RoutesStockOutThroughMutationService()
    {
        var mocks = new Mocks();
        mocks.Sale.Setup(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sale s, CancellationToken _) => s);
        mocks.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        // Le produit vendu est une monture (en stock, non commandée au fournisseur).
        mocks.Product.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { ProductId = 1, Category = ProductCategoryEnum.MONTURE, StockQuantity = 10 });
        mocks.StockMutation.Setup(s => s.DecrementStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var viewModel = mocks.Build(new PassThroughTransactionRunner());
        viewModel.IsCounterSale = true; // vente comptoir immédiate → décrément de stock
        viewModel.AddOrderItem(FrameItem());

        Sale? saved = null;
        viewModel.OrderSaved += (_, s) => saved = s;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        // Le décrément atomique conditionnel est utilisé (et NON l'ancien read-modify-write via UpdateAsync).
        mocks.StockMutation.Verify(s => s.DecrementStockAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
        mocks.Product.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.NotNull(saved);
        Assert.Null(viewModel.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (P2A-1D / 7) Stock insuffisant → message contrôlé, vente non notifiée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteSave_CounterSale_InsufficientStock_ShowsControlledMessage_AndDoesNotNotifySaved()
    {
        var mocks = new Mocks();
        mocks.Sale.Setup(r => r.CreateAsync(It.IsAny<Sale>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sale s, CancellationToken _) => s);
        mocks.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        mocks.Product.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { ProductId = 1, Category = ProductCategoryEnum.MONTURE, StockQuantity = 0 });

        var insufficient = new InsufficientStockException(productId: 1, requestedQuantity: 1, availableQuantity: 0);
        mocks.StockMutation.Setup(s => s.DecrementStockAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(insufficient);

        var viewModel = mocks.Build(new PassThroughTransactionRunner());
        viewModel.IsCounterSale = true;
        viewModel.AddOrderItem(FrameItem());

        var saved = false;
        viewModel.OrderSaved += (_, _) => saved = true;

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(insufficient.Message, viewModel.ErrorMessage);
        Assert.False(saved, "aucune notification de succès quand le stock est insuffisant");
    }
}
