using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la gestion des inventaires.
/// </summary>
public class InventoryViewModel : BaseViewModel
{
    private readonly IProductRepository _productRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDialogService _dialogService;

    private ObservableCollection<InventoryItem> _items = new();
    private ObservableCollection<InventoryItem> _filteredItems = new();
    private ObservableCollection<Product> _products = new();
    private string _searchText = string.Empty;
    private bool _isLoading;
    private bool _isProcessing;
    private int _totalItems;
    private int _itemsWithDifference;
    private string? _errorMessage;

    public ObservableCollection<InventoryItem> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    public ObservableCollection<InventoryItem> FilteredItems
    {
        get => _filteredItems;
        set => SetProperty(ref _filteredItems, value);
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
                FilterItems();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public int TotalItems
    {
        get => _totalItems;
        set => SetProperty(ref _totalItems, value);
    }

    public int ItemsWithDifference
    {
        get => _itemsWithDifference;
        set => SetProperty(ref _itemsWithDifference, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ICommand LoadInventoryCommand { get; }
    public ICommand ScanProductCommand { get; }
    public ICommand ConfirmItemAdjustmentCommand { get; }

    public InventoryViewModel(
        IProductRepository productRepository,
        IStockMovementRepository stockMovementRepository,
        IUnitOfWork unitOfWork,
        IDialogService dialogService)
    {
        _productRepository = productRepository;
        _stockMovementRepository = stockMovementRepository;
        _unitOfWork = unitOfWork;
        _dialogService = dialogService;

        LoadInventoryCommand = new RelayCommand(async () => await LoadInventoryAsync());
        ScanProductCommand = new RelayCommand<string>(ExecuteScanProduct);
        ConfirmItemAdjustmentCommand = new RelayCommand<InventoryItem>(async (item) => await ConfirmItemAdjustmentAsync(item!));

        Title = "📋 Inventaire";

        _ = LoadInventoryAsync();
    }

    private async Task LoadInventoryAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var products = await _productRepository.GetAllAsync();
            Products = new ObservableCollection<Product>(products.OrderBy(p => p.Name));

            Items = new ObservableCollection<InventoryItem>(
                products.Select(p => new InventoryItem
                {
                    Product = p,
                    TheoreticalStock = p.StockQuantity,
                    CountedStock = 0
                }).OrderBy(i => i.Product.Name));

            FilteredItems = new ObservableCollection<InventoryItem>(Items);
            TotalItems = Items.Count;
            ItemsWithDifference = 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des produits: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[InventoryViewModel] Error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExecuteScanProduct(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return;

        var item = Items.FirstOrDefault(i =>
            i.Product.Reference?.Equals(reference, StringComparison.OrdinalIgnoreCase) == true);

        if (item != null)
        {
            item.CountedStock++;
            CalculateDifferences();
        }
    }

    private void FilterItems()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            // Show all
            FilteredItems = new ObservableCollection<InventoryItem>(Items);
            return;
        }

        var filtered = Items.Where(item =>
            item.Product.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
            item.Product.Reference?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);

        FilteredItems = new ObservableCollection<InventoryItem>(filtered);
    }

    private void CalculateDifferences()
    {
        // Recalculate the count of items with differences
    }

    private async Task ConfirmItemAdjustmentAsync(InventoryItem item)
    {
        if (item.CountedStock == item.OriginalCountedStock)
            return;

        var message = $"Confirmer l'ajustement pour {item.Product.Name} ?\n\n" +
                     $"De {item.OriginalCountedStock} à {item.CountedStock} (écart: {item.Difference:+#;-#;0})";

        var confirmed = await _dialogService.ShowConfirmationAsync("Confirmer l'ajustement", message);

        if (!confirmed)
        {
            // Revert to original
            item.CountedStock = item.OriginalCountedStock;
            return;
        }

        IsProcessing = true;
        ErrorMessage = null;

        try
        {
            // Fetch fresh product to avoid tracking conflicts
            var freshProduct = await _productRepository.GetByIdAsync(item.Product.ProductId);
            if (freshProduct == null)
            {
                throw new Exception("Produit non trouvé");
            }

            // Create adjustment movement
            var movement = new StockMovement
            {
                ProductId = freshProduct.ProductId,
                MovementType = StockMovementType.Adjustment,
                Quantity = item.CountedStock,
                Reason = $"Inventaire - Écart: {item.Difference:+#;-#;0}"
            };

            await _stockMovementRepository.CreateAsync(movement);

            // Update product stock
            freshProduct.StockQuantity = item.CountedStock;
            await _productRepository.UpdateAsync(freshProduct);

            await _unitOfWork.SaveChangesAsync();

            // Update the item in the UI - cela fera disparaître le bouton ✓
            item.OriginalCountedStock = item.CountedStock;
            item.TheoreticalStock = item.CountedStock;

            await _dialogService.ShowInformationAsync("Succès", $"Ajustement appliqué pour {item.Product.Name}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[InventoryViewModel] Error: {ex}");
            // Revert to original
            item.CountedStock = item.OriginalCountedStock;
        }
        finally
        {
            IsProcessing = false;
        }
    }
}

/// <summary>
/// Représente un produit dans l'inventaire.
/// </summary>
public class InventoryItem : BaseViewModel
{
    public Product Product { get; set; } = null!;

    private int _theoreticalStock;
    public int TheoreticalStock
    {
        get => _theoreticalStock;
        set => SetProperty(ref _theoreticalStock, value);
    }

    private int _countedStock;
    public int CountedStock
    {
        get => _countedStock;
        set
        {
            if (SetProperty(ref _countedStock, value))
            {
                OnPropertyChanged(nameof(Difference));
                OnPropertyChanged(nameof(HasDifference));
                OnPropertyChanged(nameof(CanConfirm));
            }
        }
    }

    private int _originalCountedStock;
    /// <summary>
    /// Stock comptée original (avant modification)
    /// </summary>
    public int OriginalCountedStock
    {
        get => _originalCountedStock;
        set => SetProperty(ref _originalCountedStock, value);
    }

    public int Difference => CountedStock - TheoreticalStock;
    public bool HasDifference => Difference != 0;

    /// <summary>
    /// Indique si le stock comptée a changé et peut être confirmé
    /// </summary>
    public bool CanConfirm => CountedStock != OriginalCountedStock;

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
