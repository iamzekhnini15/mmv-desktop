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
/// ViewModel pour le formulaire de mouvement de stock.
/// </summary>
public class StockMovementFormViewModel : BaseViewModel
{
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDialogService _dialogService;

    private ObservableCollection<Product> _products = new();
    private long? _selectedProductId;
    private Product? _selectedProduct;
    private string _selectedMovementType = "In";
    private int _quantity = 1;
    private string _notes = string.Empty;
    private DateTime _movementDate = DateTime.Now;
    private bool _isSaving;
    private string? _errorMessage;

    // For display
    private int _currentStock;
    private int _newStock;

    public ObservableCollection<Product> Products
    {
        get => _products;
        set => SetProperty(ref _products, value);
    }

    public long? SelectedProductId
    {
        get => _selectedProductId;
        set
        {
            if (SetProperty(ref _selectedProductId, value))
            {
                _ = OnProductSelectedAsync();
            }
        }
    }

    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set => SetProperty(ref _selectedProduct, value);
    }

    public string SelectedMovementType
    {
        get => _selectedMovementType;
        set
        {
            if (SetProperty(ref _selectedMovementType, value))
            {
                CalculateNewStock();
            }
        }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
            {
                CalculateNewStock();
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public DateTime MovementDate
    {
        get => _movementDate;
        set => SetProperty(ref _movementDate, value);
    }

    public int CurrentStock
    {
        get => _currentStock;
        set => SetProperty(ref _currentStock, value);
    }

    public int NewStock
    {
        get => _newStock;
        set => SetProperty(ref _newStock, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public ObservableCollection<string> MovementTypes { get; } = new()
    {
        "In",      // Entrée
        "Out",     // Sortie
        "Adjustment" // Ajustement
    };

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public event EventHandler? MovementSaved;
    public event EventHandler? CancelRequested;

    public StockMovementFormViewModel(
        IStockMovementRepository stockMovementRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        IDialogService dialogService)
    {
        _stockMovementRepository = stockMovementRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _dialogService = dialogService;

        SaveCommand = new RelayCommand(async () => await SaveAsync(), CanSave);
        CancelCommand = new RelayCommand(ExecuteCancel);
        
        // Recalculer CanSave quand les propriétés changent
        PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(SelectedProductId) || e.PropertyName == nameof(Quantity))
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };
    }

    public async void InitializeForCreate()
    {
        await LoadProductsAsync();
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
            ErrorMessage = $"Erreur lors du chargement des produits: {ex.Message}";
        }
    }

    private async Task OnProductSelectedAsync()
    {
        if (!SelectedProductId.HasValue)
        {
            SelectedProduct = null;
            CurrentStock = 0;
            NewStock = 0;
            return;
        }

        try
        {
            var product = await _productRepository.GetByIdAsync(SelectedProductId.Value);
            SelectedProduct = product;
            CurrentStock = product?.StockQuantity ?? 0;
            CalculateNewStock();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement du produit: {ex.Message}";
        }
    }

    private void CalculateNewStock()
    {
        if (SelectedProduct == null)
        {
            NewStock = 0;
            return;
        }

        NewStock = SelectedMovementType switch
        {
            "In" => CurrentStock + Quantity,
            "Out" => CurrentStock - Quantity,
            "Adjustment" => Quantity, // Pour ajustement, Quantity = nouveau stock absolu
            _ => CurrentStock
        };
    }

    private bool CanSave()
    {
        return SelectedProductId.HasValue && Quantity > 0;
    }

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsSaving = true;

        try
        {
            if (SelectedProduct == null)
            {
                ErrorMessage = "Veuillez sélectionner un produit.";
                return;
            }

            // Validation: OUT ne peut pas créer un stock négatif
            if (SelectedMovementType == "Out" && NewStock < 0)
            {
                var confirm = await _dialogService.ShowConfirmationAsync(
                    "Stock négatif",
                    $"Cette sortie créera un stock négatif ({NewStock}). Continuer ?");

                if (!confirm)
                    return;
            }

            // Create movement
            var movement = new StockMovement
            {
                ProductId = SelectedProductId!.Value,
                MovementType = Enum.Parse<StockMovementType>(SelectedMovementType),
                Quantity = Quantity,
                Reason = Notes
            };

            await _stockMovementRepository.CreateAsync(movement);

            // Update product stock
            if (SelectedMovementType == "Adjustment")
            {
                SelectedProduct.StockQuantity = Quantity; // Ajustement absolu
            }
            else
            {
                SelectedProduct.StockQuantity = NewStock;
            }

            await _productRepository.UpdateAsync(SelectedProduct);
            await _unitOfWork.SaveChangesAsync();

            await _dialogService.ShowConfirmationAsync("Succès", "Mouvement de stock enregistré avec succès.");
            MovementSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de l'enregistrement: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[StockMovementFormViewModel] Error: {ex}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void ExecuteCancel()
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
