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
    private string? _selectedMovementType = "Tous types";
    private long? _selectedProductId = 0;
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
        "Tous types",
        "In",
        "Out",
        "Adjustment"
    };

    public ICommand CreateMovementCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ShowDetailCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }

    private int _currentPage = 1;
    private int _totalPages = 1;

    public int CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        set => SetProperty(ref _totalPages, value);
    }

    public event EventHandler? CreateMovementRequested;
    public event EventHandler<StockMovement>? ShowDetailRequested;

    public StockMovementsListViewModel(
        IStockMovementRepository stockMovementRepository,
        IProductRepository productRepository)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;

        CreateMovementCommand = new RelayCommand(ExecuteCreateMovement);
        RefreshCommand = new RelayCommand(async () => await LoadMovementsAsync());
        ExportCommand = new RelayCommand(ExecuteExport);
        ShowDetailCommand = new RelayCommand<StockMovement>(ExecuteShowDetail);
        PreviousPageCommand = new RelayCommand(ExecutePreviousPage, CanExecutePreviousPage);
        NextPageCommand = new RelayCommand(ExecuteNextPage, CanExecuteNextPage);

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
            var productList = new ObservableCollection<Product>();
            
            // Add a dummy product for "All products" option
            var allProductsItem = new Product
            {
                ProductId = 0,
                Name = "Tous les produits",
                Reference = string.Empty,
                Description = string.Empty,
                Category = ProductCategoryEnum.MONTURE,
                SupplierId = 0,
                PurchasePrice = 0,
                SalePrice = 0
            };
            productList.Add(allProductsItem);
            
            foreach (var product in products.OrderBy(p => p.Name))
            {
                productList.Add(product);
            }
            
            Products = productList;
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

            if (!string.IsNullOrEmpty(SelectedMovementType) && SelectedMovementType != "Tous types")
            {
                if (Enum.TryParse<StockMovementType>(SelectedMovementType, out var movementType))
                {
                    filtered = filtered.Where(m => m.MovementType == movementType);
                }
            }

            if (SelectedProductId.HasValue && SelectedProductId.Value != 0)
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

    private void ExecuteExport()
    {
        // TODO: Implement export functionality
        System.Diagnostics.Debug.WriteLine("[StockMovementsListViewModel] Export requested");
    }

    private void ExecuteShowDetail(StockMovement? movement)
    {
        if (movement != null)
        {
            ShowDetailRequested?.Invoke(this, movement);
        }
    }

    private bool CanExecutePreviousPage()
    {
        return CurrentPage > 1;
    }

    private void ExecutePreviousPage()
    {
        if (CanExecutePreviousPage())
        {
            CurrentPage--;
            _ = LoadMovementsAsync();
        }
    }

    private bool CanExecuteNextPage()
    {
        return CurrentPage < TotalPages;
    }

    private void ExecuteNextPage()
    {
        if (CanExecuteNextPage())
        {
            CurrentPage++;
            _ = LoadMovementsAsync();
        }
    }
}
