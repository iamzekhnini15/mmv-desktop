using System;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour la gestion des mouvements de stock.
/// Orchestre la navigation entre liste et formulaire.
/// </summary>
public class StockMovementsViewModel : BaseViewModel
{
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDialogService _dialogService;
    private readonly ITransactionRunner _transactionRunner;
    private readonly IStockMutationService _stockMutationService;

    private StockMovementsListViewModel? _listViewModel;
    private StockMovementFormViewModel? _formViewModel;
    private bool _isShowingForm;

    public StockMovementsListViewModel ListViewModel => _listViewModel ??= CreateListViewModel();
    
    public StockMovementFormViewModel? FormViewModel
    {
        get => _formViewModel;
        private set => SetProperty(ref _formViewModel, value);
    }

    public bool IsShowingForm
    {
        get => _isShowingForm;
        private set => SetProperty(ref _isShowingForm, value);
    }

    public bool ShowList => !IsShowingForm;

    public ICommand BackToProductsCommand { get; }

    public event EventHandler? BackToProductsRequested;

    public StockMovementsViewModel(
        IStockMovementRepository stockMovementRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        IDialogService dialogService,
        ITransactionRunner transactionRunner,
        IStockMutationService stockMutationService)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _dialogService = dialogService;
        // P2A-1D-R2 : transmis au formulaire pour sécuriser les sorties manuelles de stock.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));

        BackToProductsCommand = new RelayCommand(ExecuteBackToProducts);

        // Subscribe to list events
        ListViewModel.CreateMovementRequested += OnCreateMovementRequested;
    }

    private StockMovementsListViewModel CreateListViewModel()
    {
        return new StockMovementsListViewModel(
            _stockMovementRepository,
            _productRepository);
    }

    private void OnCreateMovementRequested(object? sender, EventArgs e)
    {
        FormViewModel = new StockMovementFormViewModel(
            _stockMovementRepository,
            _productRepository,
            _unitOfWork,
            _dialogService,
            _transactionRunner,
            _stockMutationService);

        FormViewModel.InitializeForCreate();
        FormViewModel.MovementSaved += OnMovementSaved;
        FormViewModel.CancelRequested += OnFormCancelRequested;

        IsShowingForm = true;
        OnPropertyChanged(nameof(ShowList));
    }

    private async void OnMovementSaved(object? sender, EventArgs e)
    {
        IsShowingForm = false;
        OnPropertyChanged(nameof(ShowList));
        FormViewModel = null;

        // Reload list
        await ListViewModel.LoadMovementsAsync();
    }

    private void OnFormCancelRequested(object? sender, EventArgs e)
    {
        IsShowingForm = false;
        OnPropertyChanged(nameof(ShowList));
        FormViewModel = null;
    }

    private void ExecuteBackToProducts()
    {
        BackToProductsRequested?.Invoke(this, EventArgs.Empty);
    }
}
