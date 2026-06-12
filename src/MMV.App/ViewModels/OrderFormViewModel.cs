using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// Représente une ligne d'article dans le formulaire de commande.
/// </summary>
public class OrderItemLine : BaseViewModel
{
    private readonly IEnumerable<Product> _allProducts;

    private OrderItemType _itemType;
    private Product? _selectedProduct;
    private string _productSearchText = string.Empty;
    private ObservableCollection<Product> _filteredProducts = new();
    private bool _isProductPopupOpen;
    private int _quantity = 1;
    private decimal _unitPrice;

    // Paramètres optiques (verres uniquement)
    private double? _sphere;
    private double? _cylinder;
    private int? _axis;
    private double? _addition;
    private string _notes = string.Empty;

    public OrderItemLine(OrderItemType itemType, IEnumerable<Product> allProducts)
    {
        _itemType = itemType;
        _allProducts = allProducts ?? Array.Empty<Product>();
        _filteredProducts = new ObservableCollection<Product>(GetFilteredByType());
        SelectProductCommand = new RelayCommand<Product>(p => SelectedProduct = p);
    }

    public ICommand SelectProductCommand { get; }

    public OrderItemType ItemType
    {
        get => _itemType;
        set
        {
            if (SetProperty(ref _itemType, value))
            {
                OnPropertyChanged(nameof(ItemTypeDisplay));
                OnPropertyChanged(nameof(IsLens));
                UpdateProductFilter();
            }
        }
    }

    /// <summary>
    /// Libellé d'affichage du type d'article.
    /// </summary>
    public string ItemTypeDisplay => ItemType switch
    {
        OrderItemType.Frame => "🔲 Monture",
        OrderItemType.LensOd => "👁 Verre OD",
        OrderItemType.LensOg => "👁 Verre OG",
        OrderItemType.Accessory => "🔧 Accessoire",
        _ => ItemType.ToString()
    };

    /// <summary>
    /// Indique si cet article est un verre (OD ou OG).
    /// </summary>
    public bool IsLens => ItemType == OrderItemType.LensOd || ItemType == OrderItemType.LensOg;

    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                if (_selectedProduct != null)
                {
                    ProductSearchText = _selectedProduct.Reference ?? _selectedProduct.Name;
                    UnitPrice = _selectedProduct.SalePrice;
                }
                IsProductPopupOpen = false;
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(LineTotal));
            }
        }
    }

    public string ProductSearchText
    {
        get => _productSearchText;
        set
        {
            if (SetProperty(ref _productSearchText, value))
            {
                UpdateProductFilter();
                IsProductPopupOpen = !string.IsNullOrWhiteSpace(_productSearchText) && FilteredProducts.Any();
            }
        }
    }

    public ObservableCollection<Product> FilteredProducts
    {
        get => _filteredProducts;
        private set => SetProperty(ref _filteredProducts, value);
    }

    public bool IsProductPopupOpen
    {
        get => _isProductPopupOpen;
        set => SetProperty(ref _isProductPopupOpen, value);
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (SetProperty(ref _quantity, Math.Max(1, value)))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(LineTotal));
            }
        }
    }

    public decimal UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (SetProperty(ref _unitPrice, value))
            {
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(LineTotal));
            }
        }
    }

    public decimal LineTotal => Quantity * UnitPrice;

    // Paramètres optiques
    public double? Sphere
    {
        get => _sphere;
        set => SetProperty(ref _sphere, value);
    }

    public double? Cylinder
    {
        get => _cylinder;
        set => SetProperty(ref _cylinder, value);
    }

    public int? Axis
    {
        get => _axis;
        set => SetProperty(ref _axis, value);
    }

    public double? Addition
    {
        get => _addition;
        set => SetProperty(ref _addition, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsValid => SelectedProduct != null && Quantity > 0 && UnitPrice > 0;

    private void UpdateProductFilter()
    {
        var baseProducts = GetFilteredByType();
        var q = (ProductSearchText ?? string.Empty).Trim().ToLowerInvariant();

        IEnumerable<Product> result;
        if (string.IsNullOrWhiteSpace(q))
        {
            result = baseProducts;
        }
        else
        {
            result = baseProducts.Where(p =>
                (!string.IsNullOrEmpty(p.Reference) && p.Reference.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrEmpty(p.Name) && p.Name.ToLowerInvariant().Contains(q)));
        }

        FilteredProducts = new ObservableCollection<Product>(result);
    }

    /// <summary>
    /// Filtre les produits disponibles selon le type d'article.
    /// </summary>
    private IEnumerable<Product> GetFilteredByType()
    {
        return ItemType switch
        {
            OrderItemType.Frame => _allProducts.Where(p =>
                p.Category == ProductCategoryEnum.MONTURE || p.Category == ProductCategoryEnum.SOLAIRE),
            OrderItemType.LensOd or OrderItemType.LensOg => _allProducts.Where(p =>
                p.Category == ProductCategoryEnum.VERRE || p.Category == ProductCategoryEnum.LENTILLE),
            OrderItemType.Accessory => _allProducts.Where(p =>
                p.Category == ProductCategoryEnum.CLIPS || p.Category == ProductCategoryEnum.PLASTIC),
            _ => _allProducts
        };
    }
}

/// <summary>
/// ViewModel pour le formulaire de création/édition de commande.
/// </summary>
public class OrderFormViewModel : BaseViewModel
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IProductRepository _productRepository;
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INumberSequenceService _numberSequenceService;

    // Client
    private ObservableCollection<Customer> _allCustomers = new();
    private ObservableCollection<Customer> _filteredCustomers = new();
    private string _customerSearchText = string.Empty;
    private Customer? _selectedCustomer;
    private bool _isCustomerPopupOpen;

    // Ordonnance
    private ObservableCollection<Prescription> _customerPrescriptions = new();
    private Prescription? _selectedPrescription;

    // Articles
    private ObservableCollection<OrderItemLine> _orderItems = new();
    private ObservableCollection<Product> _allProducts = new();

    // Commande
    private string _orderNumber = string.Empty;
    private DateTime? _estimatedDelivery;
    private string _notes = string.Empty;
    private bool _isSaving;
    private bool _isEditMode;
    private Order? _existingOrder;

    #region Properties

    public ObservableCollection<Customer> FilteredCustomers
    {
        get => _filteredCustomers;
        set => SetProperty(ref _filteredCustomers, value);
    }

    public string CustomerSearchText
    {
        get => _customerSearchText;
        set
        {
            if (SetProperty(ref _customerSearchText, value))
            {
                FilterCustomers();
                IsCustomerPopupOpen = !string.IsNullOrWhiteSpace(_customerSearchText)
                                      && FilteredCustomers.Any()
                                      && SelectedCustomer == null;
            }
        }
    }

    public Customer? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                if (_selectedCustomer != null)
                {
                    CustomerSearchText = $"{_selectedCustomer.FirstName} {_selectedCustomer.LastName}";
                    _ = LoadCustomerPrescriptionsAsync(_selectedCustomer.CustomerId);
                }
                IsCustomerPopupOpen = false;
                OnPropertyChanged(nameof(HasSelectedCustomer));
                (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCustomerPopupOpen
    {
        get => _isCustomerPopupOpen;
        set => SetProperty(ref _isCustomerPopupOpen, value);
    }

    public bool HasSelectedCustomer => SelectedCustomer != null;

    public ObservableCollection<Prescription> CustomerPrescriptions
    {
        get => _customerPrescriptions;
        set => SetProperty(ref _customerPrescriptions, value);
    }

    public Prescription? SelectedPrescription
    {
        get => _selectedPrescription;
        set => SetProperty(ref _selectedPrescription, value);
    }

    public ObservableCollection<OrderItemLine> OrderItems
    {
        get => _orderItems;
        set => SetProperty(ref _orderItems, value);
    }

    public string OrderNumber
    {
        get => _orderNumber;
        set => SetProperty(ref _orderNumber, value);
    }

    public DateTime? EstimatedDelivery
    {
        get => _estimatedDelivery;
        set => SetProperty(ref _estimatedDelivery, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        set => SetProperty(ref _isEditMode, value);
    }

    /// <summary>
    /// Montant total calculé automatiquement.
    /// </summary>
    public decimal TotalAmount => OrderItems.Where(i => i.IsValid).Sum(i => i.LineTotal);

    #endregion

    #region Commands

    public ICommand AddFrameCommand { get; }
    public ICommand AddLensOdCommand { get; }
    public ICommand AddLensOgCommand { get; }
    public ICommand AddAccessoryCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand AutoFillFromPrescriptionCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearCustomerCommand { get; }
    public ICommand SelectCustomerCommand { get; }

    #endregion

    #region Events

    public event EventHandler? OrderSaved;
    public event EventHandler? CancelRequested;

    #endregion

    public OrderFormViewModel(
        IOrderRepository orderRepository,
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        IPrescriptionRepository prescriptionRepository,
        IUnitOfWork unitOfWork,
        INumberSequenceService numberSequenceService,
        Order? existingOrder = null)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _prescriptionRepository = prescriptionRepository;
        _unitOfWork = unitOfWork;
        // Numérotation fiable obligatoire (P2A-1E) : remplace le comptage count+1 sujet aux collisions.
        _numberSequenceService = numberSequenceService ?? throw new ArgumentNullException(nameof(numberSequenceService));
        _existingOrder = existingOrder;
        _isEditMode = existingOrder != null;

        AddFrameCommand = new RelayCommand(() => AddItem(OrderItemType.Frame));
        AddLensOdCommand = new RelayCommand(() => AddItem(OrderItemType.LensOd));
        AddLensOgCommand = new RelayCommand(() => AddItem(OrderItemType.LensOg));
        AddAccessoryCommand = new RelayCommand(() => AddItem(OrderItemType.Accessory));
        RemoveItemCommand = new RelayCommand<OrderItemLine>(RemoveItem);
        AutoFillFromPrescriptionCommand = new RelayCommand(AutoFillFromPrescription, () => SelectedPrescription != null);
        SaveCommand = new RelayCommand(async () => await SaveAsync(), CanSave);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke(this, EventArgs.Empty));
        ClearCustomerCommand = new RelayCommand(ClearCustomer);
        SelectCustomerCommand = new RelayCommand<Customer>(c => { if (c != null) SelectedCustomer = c; });

        OrderItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(TotalAmount));
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        };

        Title = _isEditMode ? "Modifier la Commande" : "Nouvelle Commande";
        EstimatedDelivery = DateTime.Now.AddDays(14);
    }

    /// <summary>
    /// Charge les données initiales (clients, produits, numéro de commande).
    /// </summary>
    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var customers = await _customerRepository.GetAllAsync();
            _allCustomers = new ObservableCollection<Customer>(customers.OrderBy(c => c.LastName));
            FilteredCustomers = new ObservableCollection<Customer>(_allCustomers);

            var products = await _productRepository.GetAllAsync();
            _allProducts = new ObservableCollection<Product>(products.OrderBy(p => p.Name));

            if (_isEditMode && _existingOrder != null)
            {
                await LoadExistingOrderAsync();
            }
            else
            {
                OrderNumber = await GenerateOrderNumberAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de l'initialisation : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrderFormViewModel] Init error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadExistingOrderAsync()
    {
        if (_existingOrder == null) return;

        OrderNumber = _existingOrder.OrderNumber;
        Notes = _existingOrder.Notes ?? string.Empty;
        EstimatedDelivery = _existingOrder.EstimatedDelivery;

        if (_existingOrder.SaleId > 0)
        {
            // Charger le client depuis la vente liée
            if (_existingOrder.Sale?.CustomerId != null)
            {
                SelectedCustomer = _allCustomers.FirstOrDefault(c => c.CustomerId == _existingOrder.Sale.CustomerId);
            }
        }

        foreach (var item in _existingOrder.OrderItems)
        {
            var line = new OrderItemLine(item.ItemType, _allProducts)
            {
                SelectedProduct = _allProducts.FirstOrDefault(p => p.ProductId == item.ProductId),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Sphere = item.Sphere,
                Cylinder = item.Cylinder,
                Axis = item.Axis,
                Addition = item.Addition,
            };
            OrderItems.Add(line);
        }

        OnPropertyChanged(nameof(TotalAmount));
    }

    /// <summary>
    /// Attribue un numéro de commande fiable (P2A-1E, R-03) via la séquence transactionnelle déterministe,
    /// en remplacement de l'ancien comptage <c>count + 1</c> (sujet aux collisions : deux formulaires ouverts
    /// simultanément obtenaient le même numéro face à l'index UNIQUE). Le numéro est attribué à l'ouverture
    /// du formulaire ; un abandon (annulation) laisse un trou de numérotation, ce qui est acceptable pour une
    /// numérotation non fiscale (cf. ADR-numbering).
    /// </summary>
    private async Task<string> GenerateOrderNumberAsync()
    {
        return await _numberSequenceService.NextNumberAsync(DocumentSequenceNames.Order);
    }

    private void FilterCustomers()
    {
        var q = (CustomerSearchText ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(q))
        {
            FilteredCustomers = new ObservableCollection<Customer>(_allCustomers);
            return;
        }

        var filtered = _allCustomers.Where(c =>
            (!string.IsNullOrEmpty(c.FirstName) && c.FirstName.ToLowerInvariant().Contains(q)) ||
            (!string.IsNullOrEmpty(c.LastName) && c.LastName.ToLowerInvariant().Contains(q)) ||
            (!string.IsNullOrEmpty(c.Phone) && c.Phone.Contains(q)) ||
            (!string.IsNullOrEmpty(c.Email) && c.Email.ToLowerInvariant().Contains(q)));

        FilteredCustomers = new ObservableCollection<Customer>(filtered);
    }

    private async Task LoadCustomerPrescriptionsAsync(long customerId)
    {
        try
        {
            var prescriptions = await _prescriptionRepository.GetByCustomerIdAsync(customerId);
            CustomerPrescriptions = new ObservableCollection<Prescription>(
                prescriptions.OrderByDescending(p => p.IssueDate));
            SelectedPrescription = CustomerPrescriptions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrderFormViewModel] Error loading prescriptions: {ex.Message}");
            CustomerPrescriptions.Clear();
        }
    }

    private void ClearCustomer()
    {
        SelectedCustomer = null;
        CustomerSearchText = string.Empty;
        CustomerPrescriptions.Clear();
        SelectedPrescription = null;
    }

    private void AddItem(OrderItemType type)
    {
        var line = new OrderItemLine(type, _allProducts);
        OrderItems.Add(line);
        OnPropertyChanged(nameof(TotalAmount));
    }

    private void RemoveItem(OrderItemLine? item)
    {
        if (item != null)
        {
            OrderItems.Remove(item);
            OnPropertyChanged(nameof(TotalAmount));
        }
    }

    /// <summary>
    /// Auto-remplit les paramètres optiques des verres OD/OG depuis l'ordonnance sélectionnée.
    /// </summary>
    private void AutoFillFromPrescription()
    {
        if (SelectedPrescription == null) return;

        foreach (var item in OrderItems)
        {
            if (item.ItemType == OrderItemType.LensOd)
            {
                item.Sphere = SelectedPrescription.OdSphere;
                item.Cylinder = SelectedPrescription.OdCylinder;
                item.Axis = SelectedPrescription.OdAxis;
                item.Addition = SelectedPrescription.OdAddition;
            }
            else if (item.ItemType == OrderItemType.LensOg)
            {
                item.Sphere = SelectedPrescription.OgSphere;
                item.Cylinder = SelectedPrescription.OgCylinder;
                item.Axis = SelectedPrescription.OgAxis;
                item.Addition = SelectedPrescription.OgAddition;
            }
        }

        (AutoFillFromPrescriptionCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private bool CanSave()
    {
        return SelectedCustomer != null
               && OrderItems.Count > 0
               && OrderItems.All(i => i.IsValid)
               && !IsSaving;
    }

    private async Task SaveAsync()
    {
        if (!CanSave()) return;

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            var order = _existingOrder ?? new Order();
            order.OrderNumber = OrderNumber;
            order.EstimatedDelivery = EstimatedDelivery;
            order.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
            // Note: Order n'a plus CustomerId/TotalAmount - ces propriétés sont sur Sale

            if (!_isEditMode)
            {
                order.OrderDate = DateTime.UtcNow;
                order.Status = OrderStatus.New;
            }

            // Construire les articles
            order.OrderItems.Clear();
            foreach (var line in OrderItems.Where(i => i.IsValid))
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = line.SelectedProduct!.ProductId,
                    ItemType = line.ItemType,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Sphere = line.IsLens ? line.Sphere : null,
                    Cylinder = line.IsLens ? line.Cylinder : null,
                    Axis = line.IsLens ? line.Axis : null,
                    Addition = line.IsLens ? line.Addition : null,
                });
            }

            if (_isEditMode)
            {
                await _orderRepository.UpdateAsync(order);
            }
            else
            {
                await _orderRepository.CreateAsync(order);
            }

            await _unitOfWork.SaveChangesAsync();
            OrderSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrderFormViewModel] Save error: {ex}");
        }
        finally
        {
            IsSaving = false;
        }
    }
}
