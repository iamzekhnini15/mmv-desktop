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
/// ViewModel pour la liste des produits avec recherche et pagination.
/// </summary>
public class ProductsListViewModel : BaseViewModel
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    private ObservableCollection<Product> _products;
    private ObservableCollection<Product> _filteredProducts;
    private ObservableCollection<string> _categories;
    private Product? _selectedProduct;
    private string? _selectedCategory;
    private string _searchText = string.Empty;
    private int _currentPage = 1;
    private int _pageSize = 20;
    private int _totalProducts;
    private bool _showLowStockOnly;

    private ICommand? _createCommand;
    private ICommand? _editCommand;
    private ICommand? _deleteCommand;
    private ICommand? _refreshCommand;
    private ICommand? _nextPageCommand;
    private ICommand? _previousPageCommand;
    private ICommand? _manageCategoriesCommand;
    private ICommand? _manageSuppliersCommand;

    /// <summary>
    /// Collection de tous les produits.
    /// </summary>
    public ObservableCollection<Product> Products
    {
        get => _products;
        set => SetProperty(ref _products, value);
    }

    /// <summary>
    /// Collection des produits filtrés.
    /// </summary>
    public ObservableCollection<Product> FilteredProducts
    {
        get => _filteredProducts;
        set => SetProperty(ref _filteredProducts, value);
    }

    /// <summary>
    /// Collection des catégories pour le filtre.
    /// </summary>
    public ObservableCollection<string> Categories
    {
        get => _categories;
        set => SetProperty(ref _categories, value);
    }

    /// <summary>
    /// Produit sélectionné dans la liste.
    /// </summary>
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                (EditCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Catégorie sélectionnée pour le filtre.
    /// </summary>
    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
                ApplyFilter();
        }
    }

    /// <summary>
    /// Texte de recherche.
    /// </summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    /// <summary>
    /// Page actuelle.
    /// </summary>
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

    /// <summary>
    /// Nombre d'éléments par page.
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set => SetProperty(ref _pageSize, value);
    }

    /// <summary>
    /// Nombre total de produits.
    /// </summary>
    public int TotalProducts
    {
        get => _totalProducts;
        set
        {
            if (SetProperty(ref _totalProducts, value))
                OnPropertyChanged(nameof(TotalPages));
        }
    }

    /// <summary>
    /// Nombre total de pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalProducts / PageSize);

    /// <summary>
    /// Indique si on affiche uniquement les produits en stock bas.
    /// </summary>
    public bool ShowLowStockOnly
    {
        get => _showLowStockOnly;
        set
        {
            if (SetProperty(ref _showLowStockOnly, value))
                ApplyFilter();
        }
    }

    /// <summary>
    /// Nombre de résultats affichés après filtrage.
    /// </summary>
    public int FilteredCount => FilteredProducts?.Count ?? 0;

    /// <summary>
    /// Indique si on peut aller à la page précédente.
    /// </summary>
    public bool CanGoToPreviousPage => CurrentPage > 1;

    /// <summary>
    /// Indique si on peut aller à la page suivante.
    /// </summary>
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    // Événements
    public event EventHandler? CreateProductRequested;
    public event EventHandler<Product>? EditProductRequested;

    // Commandes
    public ICommand CreateCommand => _createCommand ??= new RelayCommand(ExecuteCreate);
    public ICommand EditCommand => _editCommand ??= new RelayCommand(ExecuteEdit, CanEdit);
    public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(ExecuteDelete, CanDelete);
    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(async () => await LoadProductsAsync());
    public ICommand NextPageCommand => _nextPageCommand ??= new RelayCommand(ExecuteNextPage, () => CanGoToNextPage);
    public ICommand PreviousPageCommand => _previousPageCommand ??= new RelayCommand(ExecutePreviousPage, () => CanGoToPreviousPage);
    public ICommand ManageCategoriesCommand => _manageCategoriesCommand ??= new RelayCommand(ExecuteManageCategories);
    public ICommand ManageSuppliersCommand => _manageSuppliersCommand ??= new RelayCommand(ExecuteManageSuppliers);

    public ProductsListViewModel(
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;

        _products = new ObservableCollection<Product>();
        _filteredProducts = new ObservableCollection<Product>();
        _categories = new ObservableCollection<string>();

        // Charger les données au démarrage
        _ = LoadProductsAsync();
        LoadCategories();
    }

    /// <summary>
    /// Charge tous les produits depuis le repository.
    /// </summary>
    public async Task LoadProductsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var products = await _productRepository.GetAllAsync() ?? Array.Empty<Product>();
            System.Diagnostics.Debug.WriteLine($"[ProductsListViewModel] Repo returned {products.Count} products");
            Console.WriteLine($"[ProductsListViewModel] Repo returned {products.Count} products");

            Products.Clear();
            foreach (var product in products)
            {
                Products.Add(product);
                System.Diagnostics.Debug.WriteLine($"[ProductsListViewModel] Loaded product: {product.Reference} - {product.Name}");
                Console.WriteLine($"[ProductsListViewModel] Loaded product: {product.Reference} - {product.Name}");
            }

            TotalProducts = Products.Count;
            System.Diagnostics.Debug.WriteLine($"[ProductsListViewModel] TotalProducts set to {TotalProducts}");
            Console.WriteLine($"[ProductsListViewModel] TotalProducts set to {TotalProducts}");
            ApplyFilter();
            System.Diagnostics.Debug.WriteLine($"[ProductsListViewModel] FilteredProducts count after ApplyFilter: {FilteredProducts?.Count ?? 0}");
            Console.WriteLine($"[ProductsListViewModel] FilteredProducts count after ApplyFilter: {FilteredProducts?.Count ?? 0}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ProductsListViewModel] ERROR loading products: {ex}");
            Console.WriteLine($"[ProductsListViewModel] ERROR loading products: {ex}");
            ErrorMessage = $"Erreur lors du chargement des produits : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Charge toutes les catégories depuis l'énumération.
    /// </summary>
    private void LoadCategories()
    {
        try
        {
            Categories.Clear();
            // Ajouter l'option "Toutes les catégories"
            Categories.Add("Toutes les catégories");
            // Charger toutes les valeurs de l'énumération
            foreach (ProductCategoryEnum category in Enum.GetValues(typeof(ProductCategoryEnum)))
            {
                Categories.Add(category.ToString());
            }
            SelectedCategory = "Toutes les catégories"; // Par défaut, toutes les catégories
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des catégories : {ex.Message}";
        }
    }

    /// <summary>
    /// Applique les filtres de recherche et de pagination.
    /// </summary>
    private void ApplyFilter()
    {
        if (Products == null) return;

        var filtered = Products.AsEnumerable();

        // Filtre par catégorie
        if (!string.IsNullOrEmpty(SelectedCategory) && SelectedCategory != "Toutes les catégories")
        {
            if (Enum.TryParse<ProductCategoryEnum>(SelectedCategory, out var categoryEnum))
            {
                filtered = filtered.Where(p => p.Category == categoryEnum);
            }
        }

        // Filtre par stock bas
        if (ShowLowStockOnly)
        {
            filtered = filtered.Where(p => p.StockQuantity <= p.StockAlertThreshold);
        }

        // Filtre par recherche
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLower();
            filtered = filtered.Where(p =>
                (p.Name?.ToLower().Contains(search) ?? false) ||
                (p.Reference?.ToLower().Contains(search) ?? false) ||
                (p.Description?.ToLower().Contains(search) ?? false));
        }

        // Pagination
        var paginatedResults = filtered
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        FilteredProducts.Clear();
        foreach (var product in paginatedResults)
        {
            FilteredProducts.Add(product);
        }

        OnPropertyChanged(nameof(FilteredCount));
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteCreate()
    {
        CreateProductRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanEdit() => SelectedProduct != null;

    private void ExecuteEdit()
    {
        if (SelectedProduct != null)
        {
            EditProductRequested?.Invoke(this, SelectedProduct);
        }
    }

    private bool CanDelete() => SelectedProduct != null;

    private async void ExecuteDelete()
    {
        if (SelectedProduct == null) return;

        IsLoading = true;
        try
        {
            await _productRepository.DeleteAsync(SelectedProduct.ProductId);
            await _unitOfWork.SaveChangesAsync();
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la suppression : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExecuteNextPage()
    {
        if (CanGoToNextPage)
        {
            CurrentPage++;
        }
    }

    private void ExecutePreviousPage()
    {
        if (CanGoToPreviousPage)
        {
            CurrentPage--;
        }
    }

    private void ExecuteManageCategories()
    {
        // TODO: Implémenter la gestion des catégories
    }

    private void ExecuteManageSuppliers()
    {
        // TODO: Implémenter la gestion des fournisseurs
    }
}
