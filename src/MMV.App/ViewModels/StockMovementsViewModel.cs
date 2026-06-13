using System;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Stock.CreateStockMovement;
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
    private readonly IDialogService _dialogService;
    private readonly ICreateStockMovementUseCase _createStockMovementUseCase;

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
        IDialogService dialogService,
        ICreateStockMovementUseCase createStockMovementUseCase)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;
        _dialogService = dialogService;
        // P2B-2F : transmis au formulaire pour déléguer la création de mouvement manuel au use case applicatif.
        _createStockMovementUseCase = createStockMovementUseCase ?? throw new ArgumentNullException(nameof(createStockMovementUseCase));

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
            _productRepository,
            _dialogService,
            _createStockMovementUseCase);

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
