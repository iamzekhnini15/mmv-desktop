using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Produits & Stock.
/// Coordonne l'affichage entre la liste, le formulaire et les détails des produits.
/// </summary>
public class ProductsViewModel : BaseViewModel
{
    private ProductsListViewModel _productsListViewModel;
    private ProductFormViewModel? _productFormViewModel;
    private ProductDetailViewModel? _productDetailViewModel;
    private bool _isInEditMode;
    private bool _isCreatingNew;
    private bool _isShowingCategories;
    private bool _isShowingSuppliers;
    private bool _isShowingDetail;
    private string _errorMessage = string.Empty;
    private ICommand? _viewDetailCommand;
    private readonly IProductRepository _productRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;

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
    public bool ShowList => !IsInEditMode && !IsShowingCategories && !IsShowingSuppliers && !IsShowingDetail;

    /// <summary>
    /// Commande pour afficher la fiche détaillée d'un produit.
    /// </summary>
    public ICommand ViewDetailCommand => _viewDetailCommand ??= new RelayCommand<Product>(ExecuteViewDetail, CanViewDetail);

    /// <summary>
    /// Initialise le ViewModel avec injection de dépendances.
    /// </summary>
    public ProductsViewModel(
        IProductRepository productRepository,
        IProductCategoryRepository categoryRepository,
        ISupplierRepository supplierRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _supplierRepository = supplierRepository;
        _unitOfWork = unitOfWork;

        // Initialiser le ViewModel de la liste
        _productsListViewModel = new ProductsListViewModel(productRepository, categoryRepository, unitOfWork);

        // S'abonner aux événements de la liste
        _productsListViewModel.CreateProductRequested += OnCreateProductRequested;
        _productsListViewModel.EditProductRequested += OnEditProductRequested;

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
            ProductFormViewModel = new ProductFormViewModel(_productRepository, _categoryRepository, _supplierRepository, _unitOfWork);
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
            ProductFormViewModel = new ProductFormViewModel(_productRepository, _categoryRepository, _supplierRepository, _unitOfWork, product);
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

    public void CloseDetail()
    {
        IsShowingDetail = false;
        ProductDetailViewModel = null;
    }
}
