using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des mouvements de stock avec filtres.
/// </summary>
public class StockMovementsListViewModel : BaseViewModel
{
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IProductRepository _productRepository;

    private ObservableCollection<StockMovement> _movements = new();
    private ObservableCollection<Product> _products = new();
    private string _searchText = string.Empty;
    private string? _selectedMovementType;
    private long? _selectedProductId;
    private bool _isLoading;
    private int _totalMovements;

    public ObservableCollection<StockMovement> Movements
    {
        get => _movements;
        set => SetProperty(ref _movements, value);
    }

    public ObservableCollection<Product> Products
    {
        get => _products;
        set => SetProperty(ref _products, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _ = LoadMovementsAsync();
            }
        }
    }

    public string? SelectedMovementType
    {
        get => _selectedMovementType;
        set
        {
            if (SetProperty(ref _selectedMovementType, value))
            {
                _ = LoadMovementsAsync();
            }
        }
    }

    public long? SelectedProductId
    {
        get => _selectedProductId;
        set
        {
            if (SetProperty(ref _selectedProductId, value))
            {
                _ = LoadMovementsAsync();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public int TotalMovements
    {
        get => _totalMovements;
        set => SetProperty(ref _totalMovements, value);
    }

    public ObservableCollection<string> MovementTypes { get; } = new()
    {
        "In",
        "Out",
        "Adjustment"
    };

    public ICommand CreateMovementCommand { get; }
    public ICommand RefreshCommand { get; }

    public event EventHandler? CreateMovementRequested;

    public StockMovementsListViewModel(
        IStockMovementRepository stockMovementRepository,
        IProductRepository productRepository)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;

        CreateMovementCommand = new RelayCommand(ExecuteCreateMovement);
        RefreshCommand = new RelayCommand(async () => await LoadMovementsAsync());

        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadProductsAsync();
        await LoadMovementsAsync();
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            var products = await _productRepository.GetAllAsync();
            Products = new ObservableCollection<Product>(products.OrderBy(p => p.Name));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StockMovementsListViewModel] Error loading products: {ex.Message}");
        }
    }

    public async Task LoadMovementsAsync()
    {
        IsLoading = true;
        try
        {
            var allMovements = await _stockMovementRepository.GetAllAsync();

            // Apply filters
            var filtered = allMovements.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filtered = filtered.Where(m =>
                    (m.Product?.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (m.Product?.Reference?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (m.Reason?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (!string.IsNullOrEmpty(SelectedMovementType))
            {
                var movementType = Enum.Parse<StockMovementType>(SelectedMovementType);
                filtered = filtered.Where(m => m.MovementType == movementType);
            }

            if (SelectedProductId.HasValue)
            {
                filtered = filtered.Where(m => m.ProductId == SelectedProductId.Value);
            }

            var result = filtered.OrderByDescending(m => m.CreatedAt).ToList();
            
            Movements = new ObservableCollection<StockMovement>(result);
            TotalMovements = result.Count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StockMovementsListViewModel] Error loading movements: {ex.Message}");
            Movements.Clear();
            TotalMovements = 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExecuteCreateMovement()
    {
        CreateMovementRequested?.Invoke(this, EventArgs.Empty);
    }
}
