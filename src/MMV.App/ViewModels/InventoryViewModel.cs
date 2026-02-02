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
    public ICommand ApplyAdjustmentsCommand { get; }
    public ICommand ResetCommand { get; }

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
        ApplyAdjustmentsCommand = new RelayCommand(async () => await ApplyAdjustmentsAsync(), CanApplyAdjustments);
        ResetCommand = new RelayCommand(ExecuteReset);

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
        ItemsWithDifference = Items.Count(i => i.Difference != 0);
        (ApplyAdjustmentsCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool CanApplyAdjustments()
    {
        return ItemsWithDifference > 0 && !IsProcessing;
    }

    private async Task ApplyAdjustmentsAsync()
    {
        var itemsToAdjust = Items.Where(i => i.Difference != 0).ToList();

        if (itemsToAdjust.Count == 0)
            return;

        var message = $"Voulez-vous appliquer les ajustements pour {itemsToAdjust.Count} produit(s) ?\n\n" +
                     $"Cela créera des mouvements de stock de type ADJUSTMENT.";

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Confirmer les ajustements",
            message);

        if (!confirmed)
            return;

        IsProcessing = true;
        ErrorMessage = null;

        try
        {
            foreach (var item in itemsToAdjust)
            {
                // Create adjustment movement
                var movement = new StockMovement
                {
                    ProductId = item.Product.ProductId,
                    MovementType = StockMovementType.Adjustment,
                    Quantity = item.CountedStock,
                    Reason = $"Inventaire - Écart: {item.Difference:+#;-#;0}"
                };

                await _stockMovementRepository.CreateAsync(movement);

                // Update product stock
                item.Product.StockQuantity = item.CountedStock;
                await _productRepository.UpdateAsync(item.Product);

                // Update theoretical stock
                item.TheoreticalStock = item.CountedStock;
            }

            await _unitOfWork.SaveChangesAsync();

            await _dialogService.ShowConfirmationAsync("Succès", $"{itemsToAdjust.Count} ajustement(s) appliqué(s) avec succès.");

            // Reload
            await LoadInventoryAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de l'application des ajustements: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[InventoryViewModel] Error: {ex}");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private void ExecuteReset()
    {
        foreach (var item in Items)
        {
            item.CountedStock = 0;
        }
        CalculateDifferences();
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
            }
        }
    }

    public int Difference => CountedStock - TheoreticalStock;
    public bool HasDifference => Difference != 0;

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
