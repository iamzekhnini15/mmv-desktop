using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// Représente une ligne de mouvement de stock dans le formulaire multi-produits.
/// </summary>
public class ProductMovementLine : BaseViewModel
{
    private readonly IEnumerable<Product> _allProducts;

    private Product? _product;
    private string _movementType = "In";
    private int _quantity = 1;
    private string _notes = string.Empty;
    private string _searchText = string.Empty;

    private ObservableCollection<Product> _filteredProducts = new();
    private bool _isPopupOpen;

    public ProductMovementLine(IEnumerable<Product> allProducts)
    {
        _allProducts = allProducts ?? Array.Empty<Product>();
        _filteredProducts = new ObservableCollection<Product>(_allProducts);
        _isPopupOpen = false;
    }

    public Product? Product
    {
        get => _product;
        set
        {
            if (SetProperty(ref _product, value))
            {
                // When a product is selected, reflect it in the search box
                if (_product != null)
                    SearchText = _product.Reference ?? _product.Name;

                OnPropertyChanged(nameof(IsValid));
                // close suggestions when selected
                IsPopupOpen = false;
            }
        }
    }

    public string MovementType
    {
        get => _movementType;
        set => SetProperty(ref _movementType, value);
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, value))
            {
                OnPropertyChanged(nameof(IsValid));
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>
    /// Texte de recherche (référence, nom, marque/fournisseur...).
    /// Modifier cette valeur filtre la collection <see cref="FilteredProducts"/>.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                UpdateFilter();
                // open suggestions when typing
                IsPopupOpen = !string.IsNullOrWhiteSpace(_searchText) && FilteredProducts.Any();
            }
        }
    }

    public ObservableCollection<Product> FilteredProducts
    {
        get => _filteredProducts;
        private set => SetProperty(ref _filteredProducts, value);
    }

    public bool IsPopupOpen
    {
        get => _isPopupOpen;
        set => SetProperty(ref _isPopupOpen, value);
    }

    private void UpdateFilter()
    {
        var q = (SearchText ?? string.Empty).Trim();
        IEnumerable<Product> result;

        if (string.IsNullOrWhiteSpace(q))
        {
            result = _allProducts;
        }
        else
        {
            q = q.ToLowerInvariant();
            result = _allProducts.Where(p =>
                (!string.IsNullOrEmpty(p.Reference) && p.Reference.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrEmpty(p.Name) && p.Name.ToLowerInvariant().Contains(q)) ||
                (p.Supplier != null && !string.IsNullOrEmpty(p.Supplier.Name) && p.Supplier.Name.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrEmpty(p.Description) && p.Description.ToLowerInvariant().Contains(q))
            );
        }

        // Refresh collection
        FilteredProducts = new ObservableCollection<Product>(result);
    }

    public bool IsValid => Product != null && Quantity > 0;
}

/// <summary>
/// ViewModel pour le formulaire de mouvement de stock multi-produits.
/// </summary>
public class StockMovementFormViewModel : BaseViewModel
{
    private readonly IProductRepository _productRepository;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// Use case applicatif (P2B-2F) : crée un mouvement manuel de stock et applique son effet sur le produit
    /// (décrément sûr / incrément / ajustement) de façon atomique. La frontière transactionnelle et le décrément
    /// sûr (P2A-1D-R2) vivent désormais dans le use case, plus dans cette ViewModel. <b>Obligatoire</b>.
    /// </summary>
    private readonly ICreateStockMovementUseCase _createStockMovementUseCase;

    private ObservableCollection<Product> _products = new();
    private ObservableCollection<ProductMovementLine> _movementLines = new();
    private bool _isSaving;

    public ObservableCollection<Product> Products
    {
        get => _products;
        set => SetProperty(ref _products, value);
    }

    public ObservableCollection<ProductMovementLine> MovementLines
    {
        get => _movementLines;
        set => SetProperty(ref _movementLines, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    public ObservableCollection<string> MovementTypes { get; } = new()
    {
        "In",      // Entrée
        "Out",     // Sortie
        "Adjustment" // Ajustement
    };

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public event EventHandler? MovementSaved;
    public event EventHandler? CancelRequested;

    public StockMovementFormViewModel(
        IProductRepository productRepository,
        IDialogService dialogService,
        ICreateStockMovementUseCase createStockMovementUseCase)
    {
        _productRepository = productRepository;
        _dialogService = dialogService;
        // P2B-2F : la création de mouvement manuel (frontière transactionnelle + décrément sûr P2A-1D-R2) est
        // déléguée au use case applicatif, désormais obligatoire.
        _createStockMovementUseCase = createStockMovementUseCase ?? throw new ArgumentNullException(nameof(createStockMovementUseCase));

        AddLineCommand = new RelayCommand(ExecuteAddLine);
        RemoveLineCommand = new RelayCommand<ProductMovementLine>(ExecuteRemoveLine);
        SaveCommand = new RelayCommand(async () => await SaveAsync(), CanSave);
        CancelCommand = new RelayCommand(ExecuteCancel);
        
        // Monitor lines collection for CanSave
        MovementLines.CollectionChanged += (s, e) => (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    public async void InitializeForCreate()
    {
        await LoadProductsAsync();
        // Ajouter une première ligne vide
        ExecuteAddLine();
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

    private void ExecuteAddLine()
    {
        MovementLines.Add(new ProductMovementLine(Products));
    }

    private void ExecuteRemoveLine(ProductMovementLine? line)
    {
        if (line != null)
        {
            MovementLines.Remove(line);
        }
    }

    private bool CanSave()
    {
        return MovementLines.Any(l => l.IsValid);
    }

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsSaving = true;

        try
        {
            var validLines = MovementLines.Where(l => l.IsValid).ToList();
            
            if (validLines.Count == 0)
            {
                ErrorMessage = "Veuillez ajouter au moins un produit valide.";
                return;
            }

            int successCount = 0;
            int errorCount = 0;
            string? controlledError = null;

            foreach (var line in validLines)
            {
                try
                {
                    // P2B-2F : la création du mouvement manuel (rechargement produit, frontière transactionnelle,
                    // décrément sûr / incrément / ajustement, création du mouvement, SaveChanges) est déléguée au
                    // use case applicatif. La ViewModel ne fait plus que construire la commande, compter les
                    // résultats et restituer les messages — comportement utilisateur inchangé (P2A-1D-R2 préservé).
                    var command = new CreateStockMovementCommand
                    {
                        ProductId = line.Product!.ProductId,
                        MovementType = Enum.Parse<StockMovementType>(line.MovementType),
                        Quantity = line.Quantity,
                        Reason = string.IsNullOrWhiteSpace(line.Notes) ? $"Mouvement {line.MovementType}" : line.Notes,
                    };

                    var result = await _createStockMovementUseCase.ExecuteAsync(command);
                    if (!result.ProductFound)
                    {
                        // Produit introuvable : ligne comptée en erreur (comme l'ancien `continue`).
                        errorCount++;
                        continue;
                    }

                    successCount++;
                }
                catch (InsufficientStockException isex)
                {
                    // Sortie refusée : stock insuffisant. Message utilisateur contrôlé, aucune écriture.
                    errorCount++;
                    controlledError = isex.Message;
                }
                catch (PersistenceException pex)
                {
                    // Erreur de persistance contrôlée : message assaini, transaction annulée.
                    errorCount++;
                    controlledError = pex.Message;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[StockMovementFormViewModel] Error processing line: {ex}");
                    errorCount++;
                }
            }

            var message = successCount > 0
                ? $"{successCount} mouvement(s) enregistré(s) avec succès."
                : "Aucun mouvement enregistré.";

            if (errorCount > 0)
            {
                message += $"\n{errorCount} erreur(s) détectée(s).";
                if (controlledError != null)
                {
                    message += $"\n{controlledError}";
                    // Surface le message contrôlé (stock insuffisant) au formulaire.
                    ErrorMessage = controlledError;
                }
            }

            await _dialogService.ShowInformationAsync("Résultat", message);

            if (successCount > 0)
            {
                MovementSaved?.Invoke(this, EventArgs.Empty);
            }
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
