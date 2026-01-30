using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création/édition de produit.
/// </summary>
public class ProductFormViewModel : BaseViewModel
{
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly Product? _existingProduct;

    private long _productId;
    private string _reference = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private decimal _purchasePrice;
    private decimal _salePrice;
    private int _stockQuantity;
    private int _minStockLevel = 5;
    private long? _categoryId;
    private long? _supplierId;
    private ObservableCollection<ProductCategory> _categories;
    private ObservableCollection<Supplier> _suppliers;
    private ProductCategory? _selectedCategory;
    private Supplier? _selectedSupplier;

    // Messages d'erreur de validation
    private string _referenceError = string.Empty;
    private string _nameError = string.Empty;
    private string _purchasePriceError = string.Empty;
    private string _salePriceError = string.Empty;
    private string _stockQuantityError = string.Empty;
    private string _minStockLevelError = string.Empty;

    private ICommand? _saveCommand;
    private ICommand? _cancelCommand;

    public event EventHandler? ProductSaved;
    public event EventHandler? Cancelled;

    #region Propriétés du formulaire

    public long ProductId
    {
        get => _productId;
        set => SetProperty(ref _productId, value);
    }

    public string Reference
    {
        get => _reference;
        set
        {
            if (SetProperty(ref _reference, value))
                ValidateReference();
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                ValidateName();
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public decimal PurchasePrice
    {
        get => _purchasePrice;
        set
        {
            if (SetProperty(ref _purchasePrice, value))
            {
                ValidatePurchasePrice();
                OnPropertyChanged(nameof(Margin));
                OnPropertyChanged(nameof(MarginPercentage));
            }
        }
    }

    public decimal SalePrice
    {
        get => _salePrice;
        set
        {
            if (SetProperty(ref _salePrice, value))
            {
                ValidateSalePrice();
                OnPropertyChanged(nameof(Margin));
                OnPropertyChanged(nameof(MarginPercentage));
            }
        }
    }

    public int StockQuantity
    {
        get => _stockQuantity;
        set
        {
            if (SetProperty(ref _stockQuantity, value))
                ValidateStockQuantity();
        }
    }

    public int StockAlertThreshold
    {
        get => _minStockLevel;
        set
        {
            if (SetProperty(ref _minStockLevel, value))
                ValidateStockAlertThreshold();
        }
    }

    public long? CategoryId
    {
        get => _categoryId;
        set => SetProperty(ref _categoryId, value);
    }

    public long? SupplierId
    {
        get => _supplierId;
        set => SetProperty(ref _supplierId, value);
    }

    public ObservableCollection<ProductCategory> Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value);
    }

    public ObservableCollection<Supplier> Suppliers
    {
        get => _suppliers;
        set => SetProperty(ref _suppliers, value);
    }

    public ProductCategory? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                CategoryId = value?.CategoryId;
        }
    }

    public Supplier? SelectedSupplier
    {
        get => _selectedSupplier;
        set
        {
            if (SetProperty(ref _selectedSupplier, value))
                SupplierId = value?.SupplierId;
        }
    }

    /// <summary>
    /// Calcul de la marge en euros.
    /// </summary>
    public decimal Margin => SalePrice - PurchasePrice;

    /// <summary>
    /// Calcul de la marge en pourcentage.
    /// </summary>
    public string MarginPercentage
    {
        get
        {
            if (PurchasePrice == 0) return "0%";
            var percentage = ((SalePrice - PurchasePrice) / PurchasePrice) * 100;
            return $"{percentage:F1}%";
        }
    }

    #endregion

    #region Messages d'erreur

    public string ReferenceError
    {
        get => _referenceError;
        set => SetProperty(ref _referenceError, value);
    }

    public string NameError
    {
        get => _nameError;
        set => SetProperty(ref _nameError, value);
    }

    public string PurchasePriceError
    {
        get => _purchasePriceError;
        set => SetProperty(ref _purchasePriceError, value);
    }

    public string SalePriceError
    {
        get => _salePriceError;
        set => SetProperty(ref _salePriceError, value);
    }

    public string StockQuantityError
    {
        get => _stockQuantityError;
        set => SetProperty(ref _stockQuantityError, value);
    }

    public string StockAlertThresholdError
    {
        get => _minStockLevelError;
        set => SetProperty(ref _minStockLevelError, value);
    }

    #endregion

    #region Propriétés calculées

    public bool IsEditMode => _existingProduct != null;
    public string FormTitle => IsEditMode ? "✏️ Modifier un produit" : "➕ Nouveau produit";
    public bool IsValid =>
        string.IsNullOrEmpty(ReferenceError) &&
        string.IsNullOrEmpty(NameError) &&
        string.IsNullOrEmpty(PurchasePriceError) &&
        string.IsNullOrEmpty(SalePriceError) &&
        string.IsNullOrEmpty(StockQuantityError) &&
        string.IsNullOrEmpty(StockAlertThresholdError);

    #endregion

    #region Commandes

    public ICommand SaveCommand => _saveCommand ??= new RelayCommand(async () => await ExecuteSaveAsync(), () => IsValid);
    public ICommand CancelCommand => _cancelCommand ??= new RelayCommand(ExecuteCancel);

    #endregion

    /// <summary>
    /// Constructeur pour création d'un nouveau produit.
    /// </summary>
    public ProductFormViewModel(
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        ISupplierRepository supplierRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _supplierRepository = supplierRepository;
        _unitOfWork = unitOfWork;

        _categories = new ObservableCollection<ProductCategory>();
        _suppliers = new ObservableCollection<Supplier>();

        Title = "Nouveau produit";
    }

    /// <summary>
    /// Constructeur pour édition d'un produit existant.
    /// </summary>
    public ProductFormViewModel(
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        ISupplierRepository supplierRepository,
        IUnitOfWork unitOfWork,
        Product product) : this(productRepository, categoryRepository, supplierRepository, unitOfWork)
    {
        _existingProduct = product;
        Title = "Modifier produit";

        // Remplir le formulaire avec les données existantes
        ProductId = product.ProductId;
        Reference = product.Reference;
        Name = product.Name;
        Description = product.Description ?? string.Empty;
        PurchasePrice = product.PurchasePrice;
        SalePrice = product.SalePrice;
        StockQuantity = product.StockQuantity;
        StockAlertThreshold = product.StockAlertThreshold;
        CategoryId = product.CategoryId;
        SupplierId = product.SupplierId;
    }

    /// <summary>
    /// Charge les catégories et fournisseurs depuis les repositories.
    /// Doit être appelée après la construction pour initialiser les données.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            System.Diagnostics.Debug.WriteLine("[ProductFormViewModel] Début chargement catégories...");
            var categories = await _categoryRepository.GetAllAsync();
            System.Diagnostics.Debug.WriteLine($"[ProductFormViewModel] {categories.Count} catégories chargées");
            
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            System.Diagnostics.Debug.WriteLine("[ProductFormViewModel] Début chargement fournisseurs...");
            var suppliers = await _supplierRepository.GetAllAsync();
            System.Diagnostics.Debug.WriteLine($"[ProductFormViewModel] {suppliers.Count} fournisseurs chargés");
            
            Suppliers.Clear();
            foreach (var supplier in suppliers)
            {
                Suppliers.Add(supplier);
            }

            // Sélectionner les éléments correspondants si on est en mode édition
            if (_existingProduct != null)
            {
                SelectedCategory = Categories.FirstOrDefault(c => c.CategoryId == CategoryId);
                SelectedSupplier = Suppliers.FirstOrDefault(s => s.SupplierId == SupplierId);
            }
            
            System.Diagnostics.Debug.WriteLine("[ProductFormViewModel] Chargement catégories/fournisseurs terminé");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProductFormViewModel] ERREUR chargement : {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ProductFormViewModel] Stack trace : {ex.StackTrace}");
            ErrorMessage = $"Erreur lors du chargement des données : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #region Validation

    private void ValidateReference()
    {
        ReferenceError = string.IsNullOrWhiteSpace(Reference)
            ? "La référence est requise"
            : string.Empty;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ValidateName()
    {
        NameError = string.IsNullOrWhiteSpace(Name)
            ? "Le nom est requis"
            : string.Empty;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ValidatePurchasePrice()
    {
        PurchasePriceError = PurchasePrice < 0
            ? "Le prix d'achat doit être positif"
            : string.Empty;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ValidateSalePrice()
    {
        SalePriceError = SalePrice < 0
            ? "Le prix de vente doit être positif"
            : SalePrice < PurchasePrice
                ? "Le prix de vente ne devrait pas être inférieur au prix d'achat"
                : string.Empty;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ValidateStockQuantity()
    {
        StockQuantityError = StockQuantity < 0
            ? "La quantité en stock doit être positive"
            : string.Empty;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ValidateStockAlertThreshold()
    {
        StockAlertThresholdError = StockAlertThreshold < 0
            ? "Le seuil minimum doit être positif"
            : string.Empty;
        (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    #endregion

    #region Exécution des commandes

    private async Task ExecuteSaveAsync()
    {
        // Valider tous les champs
        ValidateReference();
        ValidateName();
        ValidatePurchasePrice();
        ValidateSalePrice();
        ValidateStockQuantity();
        ValidateStockAlertThreshold();

        if (!IsValid) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            Product product;

            if (IsEditMode && _existingProduct != null)
            {
                // Mise à jour d'un produit existant
                product = _existingProduct;
                product.Reference = Reference;
                product.Name = Name;
                product.Description = Description;
                product.PurchasePrice = PurchasePrice;
                product.SalePrice = SalePrice;
                product.StockQuantity = StockQuantity;
                product.StockAlertThreshold = StockAlertThreshold;
                product.CategoryId = CategoryId;
                product.SupplierId = SupplierId;

                await _productRepository.UpdateAsync(product);
            }
            else
            {
                // Création d'un nouveau produit
                product = new Product
                {
                    Reference = Reference,
                    Name = Name,
                    Description = Description,
                    PurchasePrice = PurchasePrice,
                    SalePrice = SalePrice,
                    StockQuantity = StockQuantity,
                    StockAlertThreshold = StockAlertThreshold,
                    CategoryId = CategoryId,
                    SupplierId = SupplierId
                };

                await _productRepository.CreateAsync(product);
            }

            await _unitOfWork.SaveChangesAsync();
            ProductSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExecuteCancel()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
