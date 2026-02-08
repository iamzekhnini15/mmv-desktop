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

    private ObservableCollection<StockMovement> _movements;
    private ObservableCollection<StockMovement> _filteredMovements;
    private ObservableCollection<Product> _products = new();
    private string _searchText = string.Empty;
    private string? _selectedMovementType = "Tous types";
    private long? _selectedProductId = 0;
    private int _currentPage = 1;
    private int _pageSize = 20;
    private int _totalMovements;

    private ICommand? _createMovementCommand;
    private ICommand? _refreshCommand;
    private ICommand? _exportCommand;
    private ICommand? _showDetailCommand;
    private ICommand? _previousPageCommand;
    private ICommand? _nextPageCommand;

    public ObservableCollection<StockMovement> Movements
    {
        get => _movements;
        set => SetProperty(ref _movements, value);
    }

    public ObservableCollection<StockMovement> FilteredMovements
    {
        get => _filteredMovements;
        set => SetProperty(ref _filteredMovements, value);
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
                CurrentPage = 1;
                ApplyFilter();
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
                CurrentPage = 1;
                ApplyFilter();
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
                CurrentPage = 1;
                ApplyFilter();
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set => SetProperty(ref _pageSize, value);
    }

    public int TotalMovements
    {
        get => _totalMovements;
        set
        {
            if (SetProperty(ref _totalMovements, value))
                OnPropertyChanged(nameof(TotalPages));
        }
    }

    public int TotalPages => (int)Math.Ceiling((double)TotalMovements / PageSize);

    public int FilteredCount => FilteredMovements?.Count ?? 0;

    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public ObservableCollection<string> MovementTypes { get; } = new()
    {
        "Tous types",
        "In",
        "Out",
        "Adjustment"
    };

    public ICommand CreateMovementCommand => _createMovementCommand ??= new RelayCommand(ExecuteCreateMovement);
    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(async () => await LoadMovementsAsync());
    public ICommand ExportCommand => _exportCommand ??= new RelayCommand(ExecuteExport);
    public ICommand ShowDetailCommand => _showDetailCommand ??= new RelayCommand<StockMovement>(ExecuteShowDetail);
    public ICommand PreviousPageCommand => _previousPageCommand ??= new RelayCommand(ExecutePreviousPage, () => CanGoToPreviousPage);
    public ICommand NextPageCommand => _nextPageCommand ??= new RelayCommand(ExecuteNextPage, () => CanGoToNextPage);

    public event EventHandler? CreateMovementRequested;
    public event EventHandler<StockMovement>? ShowDetailRequested;

    public StockMovementsListViewModel(
        IStockMovementRepository stockMovementRepository,
        IProductRepository productRepository)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;

        _movements = new ObservableCollection<StockMovement>();
        _filteredMovements = new ObservableCollection<StockMovement>();

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
        ErrorMessage = string.Empty;

        try
        {
            var movements = await _stockMovementRepository.GetAllAsync() ?? Array.Empty<StockMovement>();
            Movements.Clear();
            foreach (var movement in movements)
            {
                Movements.Add(movement);
            }

            TotalMovements = Movements.Count;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des mouvements : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        if (Movements == null) return;

        var filtered = Movements.AsEnumerable();

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

        // Calculer le total APRES les filtres mais AVANT la pagination
        var filteredList = filtered.OrderByDescending(m => m.CreatedAt).ToList();
        TotalMovements = filteredList.Count;

        // Puis paginer
        var paginatedResults = filteredList
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        FilteredMovements.Clear();
        foreach (var movement in paginatedResults)
        {
            FilteredMovements.Add(movement);
        }

        OnPropertyChanged(nameof(FilteredCount));
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

    private void ExecutePreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage--;
        }
    }

    private void ExecuteNextPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPage++;
        }
    }
}
