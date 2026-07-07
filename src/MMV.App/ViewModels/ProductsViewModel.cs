using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.DeleteProduct;
using MMV.Application.UseCases.Products.ListProducts;
using MMV.Application.UseCases.Products.ListProductsForPicker;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Application.UseCases.Stock.ListStockMovements;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;
using MMV.Application.UseCases.Suppliers.ListSuppliers;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Produits & Stock.
/// Coordonne l'affichage entre la liste, le formulaire et les détails des produits.
/// <para>
/// P2C-GLOBAL : les écritures produits/fournisseurs/stock sont déléguées aux use cases Application, transmis aux
/// ViewModels enfants.
/// </para>
/// <para>
/// P2D-4 : les lectures fournisseur (formulaire produit → <see cref="IListSuppliersUseCase"/>) et stock (mouvements →
/// <see cref="IListStockMovementsUseCase"/> / <see cref="IListProductsForPickerUseCase"/>) passent par des query use
/// cases. <c>ISupplierRepository</c> et <c>IStockMovementRepository</c> ont été retirés.
/// </para>
/// <para>
/// P2D-7D : la lecture d'affichage des produits (liste → fiche → formulaire d'édition) passe par
/// <see cref="IListProductsUseCase"/>, transmis à <c>ProductsListViewModel</c> ; les écrans manipulent des
/// <see cref="ProductListItemDto"/> plats (plus d'entité EF <c>Product</c>). <c>IProductRepository</c> a été retiré.
/// </para>
/// </summary>
public class ProductsViewModel : BaseViewModel
{
    private ProductsListViewModel _productsListViewModel;
    private ProductFormViewModel? _productFormViewModel;
    private ProductDetailViewModel? _productDetailViewModel;
    private SuppliersViewModel? _suppliersViewModel;
    private bool _isInEditMode;
    private bool _isCreatingNew;
    private bool _isShowingCategories;
    private bool _isShowingSuppliers;
    private bool _isShowingDetail;
    private string _errorMessage = string.Empty;
    private ICommand? _viewDetailCommand;
    private readonly IListProductsUseCase _listProductsUseCase;
    private readonly IDialogService _dialogService;
    private readonly ICreateStockMovementUseCase _createStockMovementUseCase;
    private readonly ICreateProductUseCase _createProductUseCase;
    private readonly IUpdateProductUseCase _updateProductUseCase;
    private readonly IDeleteProductUseCase _deleteProductUseCase;
    private readonly IListSuppliersUseCase _listSuppliersUseCase;
    private readonly IGetSupplierWithProductsUseCase _getSupplierWithProductsUseCase;
    private readonly ICreateSupplierUseCase _createSupplierUseCase;
    private readonly IUpdateSupplierUseCase _updateSupplierUseCase;
    private readonly IDeleteSupplierUseCase _deleteSupplierUseCase;
    private readonly IListStockMovementsUseCase _listStockMovementsUseCase;
    private readonly IListProductsForPickerUseCase _listProductsForPickerUseCase;

    /// <summary>
    /// ViewModel pour la liste des produits.
    /// </summary>
    public ProductsListViewModel ProductsListViewModel
    {
        get => _productsListViewModel;
        set => SetProperty(ref _productsListViewModel, value);
    }

    /// <summary>
    /// ViewModel du formulaire produit.
    /// </summary>
    public ProductFormViewModel? ProductFormViewModel
    {
        get => _productFormViewModel;
        set => SetProperty(ref _productFormViewModel, value);
    }

    /// <summary>
    /// ViewModel pour la fiche détaillée du produit.
    /// </summary>
    public ProductDetailViewModel? ProductDetailViewModel
    {
        get => _productDetailViewModel;
        set => SetProperty(ref _productDetailViewModel, value);
    }

    /// <summary>
    /// ViewModel pour la gestion des fournisseurs.
    /// </summary>
    public SuppliersViewModel? SuppliersViewModel
    {
        get => _suppliersViewModel;
        set => SetProperty(ref _suppliersViewModel, value);
    }

    private StockMovementsViewModel? _stockMovementsViewModel;
    /// <summary>
    /// ViewModel pour la gestion des mouvements de stock.
    /// </summary>
    public StockMovementsViewModel? StockMovementsViewModel
    {
        get => _stockMovementsViewModel;
        set => SetProperty(ref _stockMovementsViewModel, value);
    }

    /// <summary>
    /// Indique si on est en mode édition (affiche le formulaire).
    /// </summary>
    public bool IsInEditMode
    {
        get => _isInEditMode;
        set
        {
            if (SetProperty(ref _isInEditMode, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    /// <summary>
    /// Indique si on est en train de créer un nouveau produit.
    /// </summary>
    public bool IsCreatingNew
    {
        get => _isCreatingNew;
        set => SetProperty(ref _isCreatingNew, value);
    }

    /// <summary>
    /// Indique si on affiche la gestion des catégories.
    /// </summary>
    public bool IsShowingCategories
    {
        get => _isShowingCategories;
        set
        {
            if (SetProperty(ref _isShowingCategories, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    /// <summary>
    /// Indique si on affiche la gestion des fournisseurs.
    /// </summary>
    public bool IsShowingSuppliers
    {
        get => _isShowingSuppliers;
        set
        {
            if (SetProperty(ref _isShowingSuppliers, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    private bool _isShowingStockMovements;
    /// <summary>
    /// Indique si on affiche la gestion des mouvements de stock.
    /// </summary>
    public bool IsShowingStockMovements
    {
        get => _isShowingStockMovements;
        set
        {
            if (SetProperty(ref _isShowingStockMovements, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    /// <summary>
    /// Indique si on affiche la fiche détaillée du produit.
    /// </summary>
    public bool IsShowingDetail
    {
        get => _isShowingDetail;
        set
        {
            if (SetProperty(ref _isShowingDetail, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    /// <summary>
    /// Message d'erreur à afficher à l'utilisateur.
    /// </summary>
    public new string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// Propriété calculée pour afficher la liste (quand aucun formulaire/vue n'est affiché).
    /// </summary>
    public bool ShowList => !IsInEditMode && !IsShowingCategories && !IsShowingSuppliers && !IsShowingStockMovements && !IsShowingDetail;

    /// <summary>
    /// Commande pour afficher la fiche détaillée d'un produit.
    /// </summary>
    public ICommand ViewDetailCommand => _viewDetailCommand ??= new RelayCommand<ProductListItemDto>(ExecuteViewDetail, CanViewDetail);

    /// <summary>
    /// Initialise le ViewModel avec injection de dépendances.
    /// </summary>
    public ProductsViewModel(
        IListProductsUseCase listProductsUseCase,
        IDialogService dialogService,
        ICreateStockMovementUseCase createStockMovementUseCase,
        ICreateProductUseCase createProductUseCase,
        IUpdateProductUseCase updateProductUseCase,
        IDeleteProductUseCase deleteProductUseCase,
        IListSuppliersUseCase listSuppliersUseCase,
        IGetSupplierWithProductsUseCase getSupplierWithProductsUseCase,
        ICreateSupplierUseCase createSupplierUseCase,
        IUpdateSupplierUseCase updateSupplierUseCase,
        IDeleteSupplierUseCase deleteSupplierUseCase,
        IListStockMovementsUseCase listStockMovementsUseCase,
        IListProductsForPickerUseCase listProductsForPickerUseCase)
    {
        _listProductsUseCase = listProductsUseCase ?? throw new ArgumentNullException(nameof(listProductsUseCase));
        _dialogService = dialogService;
        // P2B-2F : transmis à StockMovementsViewModel → formulaire pour déléguer la création de mouvement manuel.
        _createStockMovementUseCase = createStockMovementUseCase ?? throw new ArgumentNullException(nameof(createStockMovementUseCase));
        _createProductUseCase = createProductUseCase ?? throw new ArgumentNullException(nameof(createProductUseCase));
        _updateProductUseCase = updateProductUseCase ?? throw new ArgumentNullException(nameof(updateProductUseCase));
        _deleteProductUseCase = deleteProductUseCase ?? throw new ArgumentNullException(nameof(deleteProductUseCase));
        // P2D-2 : lectures fournisseur (liste + fiche) transmises à SuppliersViewModel via query use cases ;
        // P2D-4 : la liste fournisseur alimente aussi le sélecteur du formulaire produit (ProductFormViewModel).
        _listSuppliersUseCase = listSuppliersUseCase ?? throw new ArgumentNullException(nameof(listSuppliersUseCase));
        _getSupplierWithProductsUseCase = getSupplierWithProductsUseCase ?? throw new ArgumentNullException(nameof(getSupplierWithProductsUseCase));
        _createSupplierUseCase = createSupplierUseCase ?? throw new ArgumentNullException(nameof(createSupplierUseCase));
        _updateSupplierUseCase = updateSupplierUseCase ?? throw new ArgumentNullException(nameof(updateSupplierUseCase));
        _deleteSupplierUseCase = deleteSupplierUseCase ?? throw new ArgumentNullException(nameof(deleteSupplierUseCase));
        // P2D-4 : lectures stock (liste des mouvements + sélecteur produit) transmises à StockMovementsViewModel via
        // query use cases (plus de IStockMovementRepository / IProductRepository côté mouvements).
        _listStockMovementsUseCase = listStockMovementsUseCase ?? throw new ArgumentNullException(nameof(listStockMovementsUseCase));
        _listProductsForPickerUseCase = listProductsForPickerUseCase ?? throw new ArgumentNullException(nameof(listProductsForPickerUseCase));

        // Initialiser le ViewModel de la liste
        _productsListViewModel = new ProductsListViewModel(listProductsUseCase, deleteProductUseCase);

        // S'abonner aux événements de la liste
        _productsListViewModel.CreateProductRequested += OnCreateProductRequested;
        _productsListViewModel.EditProductRequested += OnEditProductRequested;
        _productsListViewModel.DeleteProductRequested += OnDeleteProductRequested;
        _productsListViewModel.ManageSuppliersRequested += OnManageSuppliersRequested;
        _productsListViewModel.ManageStockMovementsRequested += OnManageStockMovementsRequested;

        Title = "📦 Gestion des Produits & Stock";
    }

    /// <summary>
    /// Gère la demande de création d'un nouveau produit.
    /// </summary>
    private async void OnCreateProductRequested(object? sender, EventArgs e)
    {
        try
        {
            IsCreatingNew = true;
            ErrorMessage = string.Empty;
            ProductFormViewModel = new ProductFormViewModel(_listSuppliersUseCase, _createProductUseCase, _updateProductUseCase);
            ProductFormViewModel.ProductSaved += OnProductSaved;
            ProductFormViewModel.Cancelled += OnFormCancelled;
            
            // Initialiser les données de façon asynchrone
            await ProductFormViewModel.InitializeAsync();
            IsInEditMode = true;
        }
        catch (Exception ex)
        {
            IsCreatingNew = false;
            IsInEditMode = false;
            ProductFormViewModel = null;
            ErrorMessage = $"Erreur lors de l'ouverture du formulaire : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ProductsViewModel] Erreur création produit : {ex}");
        }
    }

    /// <summary>
    /// Gère la demande d'édition d'un produit existant.
    /// </summary>
    private async void OnEditProductRequested(object? sender, ProductListItemDto product)
    {
        try
        {
            IsCreatingNew = false;
            ErrorMessage = string.Empty;
            ProductFormViewModel = new ProductFormViewModel(_listSuppliersUseCase, _createProductUseCase, _updateProductUseCase, product);
            ProductFormViewModel.ProductSaved += OnProductSaved;
            ProductFormViewModel.Cancelled += OnFormCancelled;
            
            // Initialiser les données de façon asynchrone
            await ProductFormViewModel.InitializeAsync();
            IsInEditMode = true;
        }
        catch (Exception ex)
        {
            IsCreatingNew = false;
            IsInEditMode = false;
            ProductFormViewModel = null;
            ErrorMessage = $"Erreur lors de l'ouverture du formulaire : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[ProductsViewModel] Erreur édition produit : {ex}");
        }
    }

    /// <summary>
    /// Gère la sauvegarde d'un produit.
    /// </summary>
    private async void OnProductSaved(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        ProductFormViewModel = null;
        await ProductsListViewModel.LoadProductsAsync();
    }

    /// <summary>
    /// Gère l'annulation du formulaire.
    /// </summary>
    private void OnFormCancelled(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        ProductFormViewModel = null;
    }

    /// <summary>
    /// Gère l'ouverture de la gestion des fournisseurs.
    /// </summary>
    private void OnManageSuppliersRequested(object? sender, EventArgs e)
    {
        if (SuppliersViewModel == null)
        {
            SuppliersViewModel = new SuppliersViewModel(_listSuppliersUseCase, _getSupplierWithProductsUseCase, _createSupplierUseCase, _updateSupplierUseCase, _deleteSupplierUseCase, _dialogService);
            SuppliersViewModel.BackToProductsRequested += OnSuppliersBackRequested;
        }

        IsShowingSuppliers = true;
        IsInEditMode = false;
        IsShowingDetail = false;
    }

    private void OnSuppliersBackRequested(object? sender, EventArgs e)
    {
        IsShowingSuppliers = false;
    }

    private void OnManageStockMovementsRequested(object? sender, EventArgs e)
    {
        if (StockMovementsViewModel == null)
        {
            StockMovementsViewModel = new StockMovementsViewModel(
                _listStockMovementsUseCase,
                _listProductsForPickerUseCase,
                _dialogService,
                _createStockMovementUseCase);

            StockMovementsViewModel.BackToProductsRequested += OnStockMovementsBackRequested;
        }

        IsShowingStockMovements = true;
        IsInEditMode = false;
        IsShowingDetail = false;
        IsShowingSuppliers = false;
    }

    private void OnStockMovementsBackRequested(object? sender, EventArgs e)
    {
        IsShowingStockMovements = false;
    }

    private bool CanViewDetail(ProductListItemDto? product)
    {
        return product != null && product.ProductId > 0;
    }

    private void ExecuteViewDetail(ProductListItemDto? product)
    {
        if (product == null) return;

        ProductDetailViewModel = new ProductDetailViewModel();
        ProductDetailViewModel.Initialize(product);
        ProductDetailViewModel.BackRequested += OnDetailBackRequested;
        ProductDetailViewModel.EditRequested += OnDetailEditRequested;
        ProductDetailViewModel.DeleteRequested += OnDetailDeleteRequested;

        IsShowingDetail = true;
        IsInEditMode = false;
    }

    private void OnDetailBackRequested(object? sender, EventArgs e)
    {
        CloseDetail();
    }

    private void OnDetailEditRequested(object? sender, ProductListItemDto product)
    {
        CloseDetail();
        OnEditProductRequested(this, product);
    }

    private void OnDetailDeleteRequested(object? sender, ProductListItemDto product)
    {
        ProductsListViewModel.SelectedProduct = product;
        if (ProductsListViewModel.DeleteCommand.CanExecute(null))
        {
            ProductsListViewModel.DeleteCommand.Execute(null);
        }
        CloseDetail();
    }

    /// <summary>
    /// Gère la demande de suppression avec confirmation.
    /// </summary>
    private async void OnDeleteProductRequested(object? sender, ProductListItemDto product)
    {
        if (product == null) return;

        // Créer le message de confirmation
        var message = $"Êtes-vous sûr de vouloir supprimer le produit :\n\n" +
                     $"📦 {product.Reference} - {product.Name}\n" +
                     $"💰 Prix: {product.SalePrice:C2}\n" +
                     $"📊 Stock: {product.StockQuantity}\n\n" +
                     $"⚠️ Cette action est irréversible !";

        try
        {
            // Demander confirmation
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Confirmation de suppression",
                message);

            if (confirmed)
            {
                await ProductsListViewModel.ConfirmAndDeleteProductAsync();
                
                // Fermer la page de détails si elle est ouverte
                if (IsShowingDetail)
                {
                    CloseDetail();
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la suppression : {ex.Message}";
            await _dialogService.ShowErrorAsync(
                "Erreur de suppression",
                $"Impossible de supprimer le produit :\n{ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[ProductsViewModel] Erreur suppression : {ex}");
        }
    }

    public void CloseDetail()
    {
        IsShowingDetail = false;
        ProductDetailViewModel = null;
    }
}
