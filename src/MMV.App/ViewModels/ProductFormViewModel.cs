using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.ListProducts;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Suppliers.ListSuppliers;
using MMV.Domain.Enums;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création/édition de produit.
/// <para>
/// P2C-GLOBAL : la persistance directe (repository produit + <c>IUnitOfWork</c> + <c>SaveChangesAsync</c>) a été
/// déplacée vers <see cref="ICreateProductUseCase"/> / <see cref="IUpdateProductUseCase"/>.
/// </para>
/// <para>
/// P2D-4 : la liste déroulante fournisseurs est désormais lue via le query use case
/// <see cref="IListSuppliersUseCase"/> (réutilisé de P2D-2), qui renvoie des <see cref="SupplierListItemDto"/> plats
/// (jamais l'entité EF <c>Supplier</c>). <c>ISupplierRepository</c> a été retiré.
/// </para>
/// <para>
/// P2D-7D : le pré-remplissage en édition reçoit un <see cref="ProductListItemDto"/> (projeté par
/// <c>IListProductsUseCase</c>) au lieu de l'entité EF <c>Product</c> ; la persistance reste portée par les
/// use cases de création/mise à jour.
/// </para>
/// </summary>
public class ProductFormViewModel : BaseViewModel
{
    private readonly IListSuppliersUseCase _listSuppliersUseCase;
    private readonly ICreateProductUseCase _createProductUseCase;
    private readonly IUpdateProductUseCase _updateProductUseCase;
    private readonly ProductListItemDto? _existingProduct;

    private long _productId;
    private string _reference = string.Empty;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private decimal _purchasePrice;
    private decimal _salePrice;
    private decimal? _recommendedPrice;
    private int _stockQuantity;
    private int _minStockLevel = 5;
    private ProductCategoryEnum _selectedCategory = ProductCategoryEnum.MONTURE;
    private long? _supplierId;
    private ObservableCollection<ProductCategoryEnum> _categories;
    private ObservableCollection<SupplierListItemDto> _suppliers;
    private SupplierListItemDto? _selectedSupplier;

    // Propriétés spécifiques pour GlassDetail
    private string? _glassMaterial;
    private string? _glassType;
    private string? _glassDiameter;
    private decimal? _glassIndex;
    private decimal? _powerLimitMin;
    private decimal? _powerLimitMax;

    // Propriétés spécifiques pour LensDetail
    private string? _lensBrand;
    private string? _lensModel;
    private string? _lensMaterial;
    private string? _lensType;
    private decimal? _lensDiameter;
    private decimal? _lensBaseCurve;
    private bool _lensIsColored;
    private string? _lensDuration;

    // Propriétés spécifiques pour AccessoryDetail
    private string? _accessoryColor;
    private string? _accessorySize;
    private string? _accessoryMaterial;

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

    public ProductCategoryEnum SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                // Notifier les propriétés de visibilité
                OnPropertyChanged(nameof(IsGlassCategory));
                OnPropertyChanged(nameof(IsLensCategory));
                OnPropertyChanged(nameof(IsAccessoryCategory));
            }
        }
    }

    public decimal? RecommendedPrice
    {
        get => _recommendedPrice;
        set => SetProperty(ref _recommendedPrice, value);
    }

    public long? SupplierId
    {
        get => _supplierId;
        set => SetProperty(ref _supplierId, value);
    }

    public ObservableCollection<ProductCategoryEnum> Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value);
    }

    public ObservableCollection<SupplierListItemDto> Suppliers
    {
        get => _suppliers;
        set => SetProperty(ref _suppliers, value);
    }

    public SupplierListItemDto? SelectedSupplier
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

    #region Propriétés de visibilité

    public bool IsGlassCategory => SelectedCategory == ProductCategoryEnum.VERRE;
    public bool IsLensCategory => SelectedCategory == ProductCategoryEnum.LENTILLE;
    public bool IsAccessoryCategory => SelectedCategory == ProductCategoryEnum.MONTURE ||
                                        SelectedCategory == ProductCategoryEnum.CLIPS ||
                                        SelectedCategory == ProductCategoryEnum.PLASTIC ||
                                        SelectedCategory == ProductCategoryEnum.SOLAIRE;

    #endregion

    #region Propriétés pour GlassDetail

    public string? GlassMaterial
    {
        get => _glassMaterial;
        set => SetProperty(ref _glassMaterial, value);
    }

    public string? GlassType
    {
        get => _glassType;
        set => SetProperty(ref _glassType, value);
    }

    public string? GlassDiameter
    {
        get => _glassDiameter;
        set => SetProperty(ref _glassDiameter, value);
    }

    public decimal? GlassIndex
    {
        get => _glassIndex;
        set => SetProperty(ref _glassIndex, value);
    }

    public decimal? PowerLimitMin
    {
        get => _powerLimitMin;
        set => SetProperty(ref _powerLimitMin, value);
    }

    public decimal? PowerLimitMax
    {
        get => _powerLimitMax;
        set => SetProperty(ref _powerLimitMax, value);
    }

    #endregion

    #region Propriétés pour LensDetail

    public string? LensBrand
    {
        get => _lensBrand;
        set => SetProperty(ref _lensBrand, value);
    }

    public string? LensModel
    {
        get => _lensModel;
        set => SetProperty(ref _lensModel, value);
    }

    public string? LensMaterial
    {
        get => _lensMaterial;
        set => SetProperty(ref _lensMaterial, value);
    }

    public string? LensType
    {
        get => _lensType;
        set => SetProperty(ref _lensType, value);
    }

    public decimal? LensDiameter
    {
        get => _lensDiameter;
        set => SetProperty(ref _lensDiameter, value);
    }

    public decimal? LensBaseCurve
    {
        get => _lensBaseCurve;
        set => SetProperty(ref _lensBaseCurve, value);
    }

    public bool LensIsColored
    {
        get => _lensIsColored;
        set => SetProperty(ref _lensIsColored, value);
    }

    public string? LensDuration
    {
        get => _lensDuration;
        set => SetProperty(ref _lensDuration, value);
    }

    #endregion

    #region Propriétés pour AccessoryDetail

    public string? AccessoryColor
    {
        get => _accessoryColor;
        set => SetProperty(ref _accessoryColor, value);
    }

    public string? AccessorySize
    {
        get => _accessorySize;
        set => SetProperty(ref _accessorySize, value);
    }

    public string? AccessoryMaterial
    {
        get => _accessoryMaterial;
        set => SetProperty(ref _accessoryMaterial, value);
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
    public string FormTitle => IsEditMode ? "Modifier un produit" : "Nouveau produit";
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
    public ICommand BackCommand => CancelCommand; // Alias pour le BackButton

    #endregion

    /// <summary>
    /// Constructeur pour création d'un nouveau produit.
    /// </summary>
    public ProductFormViewModel(
        IListSuppliersUseCase listSuppliersUseCase,
        ICreateProductUseCase createProductUseCase,
        IUpdateProductUseCase updateProductUseCase)
    {
        _listSuppliersUseCase = listSuppliersUseCase ?? throw new ArgumentNullException(nameof(listSuppliersUseCase));
        _createProductUseCase = createProductUseCase ?? throw new ArgumentNullException(nameof(createProductUseCase));
        _updateProductUseCase = updateProductUseCase ?? throw new ArgumentNullException(nameof(updateProductUseCase));

        _categories = new ObservableCollection<ProductCategoryEnum>();
        _suppliers = new ObservableCollection<SupplierListItemDto>();
        
        // Initialiser avec la première catégorie par défaut
        _selectedCategory = ProductCategoryEnum.MONTURE;

        Title = "Nouveau produit";
    }

    /// <summary>
    /// Constructeur pour édition d'un produit existant.
    /// </summary>
    public ProductFormViewModel(
        IListSuppliersUseCase listSuppliersUseCase,
        ICreateProductUseCase createProductUseCase,
        IUpdateProductUseCase updateProductUseCase,
        ProductListItemDto product) : this(listSuppliersUseCase, createProductUseCase, updateProductUseCase)
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
        RecommendedPrice = product.RecommendedPrice;
        StockQuantity = product.StockQuantity;
        StockAlertThreshold = product.StockAlertThreshold;
        SelectedCategory = product.Category;
        SupplierId = product.SupplierId;

        // Charger les détails spécifiques selon la catégorie
        LoadCategorySpecificDetails(product);
    }

    /// <summary>
    /// Charge les détails spécifiques (Glass, Lens, Accessory) du produit dans les propriétés du formulaire.
    /// </summary>
    private void LoadCategorySpecificDetails(ProductListItemDto product)
    {
        switch (product.Category)
        {
            case ProductCategoryEnum.VERRE:
                if (product.GlassDetail != null)
                {
                    _glassMaterial = product.GlassDetail.Material?.ToString();
                    _glassType = product.GlassDetail.GlassType?.ToString();
                    _glassDiameter = product.GlassDetail.Diameter;
                    _glassIndex = product.GlassDetail.Index;
                    _powerLimitMin = product.GlassDetail.PowerLimitMin;
                    _powerLimitMax = product.GlassDetail.PowerLimitMax;
                }
                break;

            case ProductCategoryEnum.LENTILLE:
                if (product.LensDetail != null)
                {
                    _lensBrand = product.LensDetail.Brand;
                    _lensModel = product.LensDetail.Model;
                    _lensMaterial = product.LensDetail.Material?.ToString();
                    _lensType = product.LensDetail.LensType?.ToString();
                    _lensDiameter = product.LensDetail.Diameter;
                    _lensBaseCurve = product.LensDetail.BaseCurve;
                    _lensIsColored = product.LensDetail.IsColored;
                    _lensDuration = product.LensDetail.Duration?.ToString();
                }
                break;

            case ProductCategoryEnum.MONTURE:
            case ProductCategoryEnum.CLIPS:
            case ProductCategoryEnum.PLASTIC:
            case ProductCategoryEnum.SOLAIRE:
                if (product.AccessoryDetail != null)
                {
                    _accessoryColor = product.AccessoryDetail.Color;
                    _accessorySize = product.AccessoryDetail.Size;
                    _accessoryMaterial = product.AccessoryDetail.Material;
                }
                break;
        }
    }

    /// <summary>
    /// Charge les catégories (enum) et fournisseurs depuis les repositories.
    /// Doit être appelée après la construction pour initialiser les données.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        
        try
        {
            System.Diagnostics.Debug.WriteLine("[ProductFormViewModel] Chargement des catégories (enum)...");
            
            // Charger toutes les valeurs de l'enum ProductCategoryEnum
            Categories.Clear();
            foreach (ProductCategoryEnum category in Enum.GetValues(typeof(ProductCategoryEnum)))
            {
                Categories.Add(category);
            }
            System.Diagnostics.Debug.WriteLine($"[ProductFormViewModel] {Categories.Count} catégories chargées");

            System.Diagnostics.Debug.WriteLine("[ProductFormViewModel] Début chargement fournisseurs...");
            var suppliers = await _listSuppliersUseCase.ExecuteAsync(new ListSuppliersQuery());
            System.Diagnostics.Debug.WriteLine($"[ProductFormViewModel] {suppliers.Count} fournisseurs chargés");

            Suppliers.Clear();
            foreach (var supplier in suppliers)
            {
                Suppliers.Add(supplier);
            }

            // Sélectionner les éléments correspondants si on est en mode édition
            if (_existingProduct != null)
            {
                SelectedSupplier = Suppliers.FirstOrDefault(s => s.SupplierId == SupplierId);
            }
            else
            {
                // Par défaut, sélectionner la première catégorie
                if (Categories.Count > 0)
                {
                    SelectedCategory = Categories[0];
                }
            }
            
            System.Diagnostics.Debug.WriteLine("[ProductFormViewModel] Chargement terminé");
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
            if (IsEditMode && _existingProduct != null)
            {
                await _updateProductUseCase.ExecuteAsync(new UpdateProductCommand
                {
                    ProductId = _existingProduct.ProductId,
                    Reference = Reference,
                    Name = Name,
                    Description = Description,
                    PurchasePrice = PurchasePrice,
                    SalePrice = SalePrice,
                    RecommendedPrice = RecommendedPrice,
                    StockQuantity = StockQuantity,
                    StockAlertThreshold = StockAlertThreshold,
                    Category = SelectedCategory,
                    SupplierId = SupplierId,
                    GlassMaterial = GlassMaterial,
                    GlassType = GlassType,
                    GlassDiameter = GlassDiameter,
                    GlassIndex = GlassIndex,
                    PowerLimitMin = PowerLimitMin,
                    PowerLimitMax = PowerLimitMax,
                    LensBrand = LensBrand,
                    LensModel = LensModel,
                    LensMaterial = LensMaterial,
                    LensType = LensType,
                    LensDiameter = LensDiameter,
                    LensBaseCurve = LensBaseCurve,
                    LensIsColored = LensIsColored,
                    LensDuration = LensDuration,
                    AccessoryColor = AccessoryColor,
                    AccessorySize = AccessorySize,
                    AccessoryMaterial = AccessoryMaterial
                });
            }
            else
            {
                await _createProductUseCase.ExecuteAsync(new CreateProductCommand
                {
                    Reference = Reference,
                    Name = Name,
                    Description = Description,
                    PurchasePrice = PurchasePrice,
                    SalePrice = SalePrice,
                    RecommendedPrice = RecommendedPrice,
                    StockQuantity = StockQuantity,
                    StockAlertThreshold = StockAlertThreshold,
                    Category = SelectedCategory,
                    SupplierId = SupplierId,
                    GlassMaterial = GlassMaterial,
                    GlassType = GlassType,
                    GlassDiameter = GlassDiameter,
                    GlassIndex = GlassIndex,
                    PowerLimitMin = PowerLimitMin,
                    PowerLimitMax = PowerLimitMax,
                    LensBrand = LensBrand,
                    LensModel = LensModel,
                    LensMaterial = LensMaterial,
                    LensType = LensType,
                    LensDiameter = LensDiameter,
                    LensBaseCurve = LensBaseCurve,
                    LensIsColored = LensIsColored,
                    LensDuration = LensDuration,
                    AccessoryColor = AccessoryColor,
                    AccessorySize = AccessorySize,
                    AccessoryMaterial = AccessoryMaterial
                });
            }

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
