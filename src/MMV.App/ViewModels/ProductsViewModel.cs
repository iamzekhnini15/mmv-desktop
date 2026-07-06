using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.DeleteProduct;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Application.UseCases.Suppliers.UpdateSupplier;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Produits & Stock.
/// Coordonne l'affichage entre la liste, le formulaire et les détails des produits.
/// <para>
/// P2C-GLOBAL : les écritures produits/fournisseurs/stock sont déléguées aux use cases Application, transmis aux
/// ViewModels enfants. <see cref="IProductRepository"/>, <see cref="ISupplierRepository"/> et
/// <see cref="IStockMovementRepository"/> ne sont conservés que pour les lectures d'affichage ; <c>IUnitOfWork</c>
/// a été retiré (son seul usage résiduel, l'accès à <c>UnitOfWork.StockMovements</c>, est remplacé par l'injection
/// directe de <see cref="IStockMovementRepository"/>).
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
    private readonly IProductRepository _productRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IDialogService _dialogService;
    private readonly ICreateStockMovementUseCase _createStockMovementUseCase;
    private readonly ICreateProductUseCase _createProductUseCase;
    private readonly IUpdateProductUseCase _updateProductUseCase;
    private readonly IDeleteProductUseCase _deleteProductUseCase;
    private readonly ICreateSupplierUseCase _createSupplierUseCase;
    private readonly IUpdateSupplierUseCase _updateSupplierUseCase;
    private readonly IDeleteSupplierUseCase _deleteSupplierUseCase;

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
    public ICommand ViewDetailCommand => _viewDetailCommand ??= new RelayCommand<Product>(ExecuteViewDetail, CanViewDetail);

    /// <summary>
    /// Initialise le ViewModel avec injection de dépendances.
    /// </summary>
    public ProductsViewModel(
        IProductRepository productRepository,
        ISupplierRepository supplierRepository,
        IStockMovementRepository stockMovementRepository,
        IDialogService dialogService,
        ICreateStockMovementUseCase createStockMovementUseCase,
        ICreateProductUseCase createProductUseCase,
        IUpdateProductUseCase updateProductUseCase,
        IDeleteProductUseCase deleteProductUseCase,
        ICreateSupplierUseCase createSupplierUseCase,
        IUpdateSupplierUseCase updateSupplierUseCase,
        IDeleteSupplierUseCase deleteSupplierUseCase)
    {
        _productRepository = productRepository;
        _supplierRepository = supplierRepository;
        _stockMovementRepository = stockMovementRepository;
        _dialogService = dialogService;
        // P2B-2F : transmis à StockMovementsViewModel → formulaire pour déléguer la création de mouvement manuel.
        _createStockMovementUseCase = createStockMovementUseCase ?? throw new ArgumentNullException(nameof(createStockMovementUseCase));
        _createProductUseCase = createProductUseCase ?? throw new ArgumentNullException(nameof(createProductUseCase));
        _updateProductUseCase = updateProductUseCase ?? throw new ArgumentNullException(nameof(updateProductUseCase));
        _deleteProductUseCase = deleteProductUseCase ?? throw new ArgumentNullException(nameof(deleteProductUseCase));
        _createSupplierUseCase = createSupplierUseCase ?? throw new ArgumentNullException(nameof(createSupplierUseCase));
        _updateSupplierUseCase = updateSupplierUseCase ?? throw new ArgumentNullException(nameof(updateSupplierUseCase));
        _deleteSupplierUseCase = deleteSupplierUseCase ?? throw new ArgumentNullException(nameof(deleteSupplierUseCase));

        // Initialiser le ViewModel de la liste
        _productsListViewModel = new ProductsListViewModel(productRepository, deleteProductUseCase);

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
            ProductFormViewModel = new ProductFormViewModel(_supplierRepository, _createProductUseCase, _updateProductUseCase);
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
    private async void OnEditProductRequested(object? sender, Product product)
    {
        try
        {
            IsCreatingNew = false;
            ErrorMessage = string.Empty;
            ProductFormViewModel = new ProductFormViewModel(_supplierRepository, _createProductUseCase, _updateProductUseCase, product);
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
            SuppliersViewModel = new SuppliersViewModel(_supplierRepository, _createSupplierUseCase, _updateSupplierUseCase, _deleteSupplierUseCase, _dialogService);
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
                _stockMovementRepository,
                _productRepository,
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

    private bool CanViewDetail(Product? product)
    {
        return product != null && product.ProductId > 0;
    }

    private void ExecuteViewDetail(Product? product)
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

    private void OnDetailEditRequested(object? sender, Product product)
    {
        CloseDetail();
        OnEditProductRequested(this, product);
    }

    private void OnDetailDeleteRequested(object? sender, Product product)
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
    private async void OnDeleteProductRequested(object? sender, Product product)
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
