using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'un client avec onglets.
/// </summary>
public class CustomerDetailViewModel : BaseViewModel
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IProductRepository _productRepository;
    private readonly ISaleRepository _saleRepository; // Pour historique uniquement
    private readonly IStockMovementRepository _stockMovementRepository;
    
    private Customer? _customer;
    private int _selectedTabIndex = 0;
    private SaleFormViewModel? _saleFormViewModel;
    private CustomerPrescriptionsViewModel? _prescriptionsViewModel;
    private CustomerInfoViewModel? _customerInfoViewModel;
    private CustomerPurchaseHistoryViewModel? _purchaseHistoryViewModel;

    /// <summary>
    /// Client en cours de consultation.
    /// </summary>
    public Customer? Customer
    {
        get => _customer;
        set => SetProperty(ref _customer, value);
    }

    /// <summary>
    /// Index de l'onglet sélectionné (0: Vente, 1: Ordonnance, 2: Infos, 3: Historique).
    /// </summary>
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    /// <summary>
    /// ViewModel pour le formulaire de vente.
    /// </summary>
    public SaleFormViewModel? SaleFormViewModel
    {
        get => _saleFormViewModel;
        set => SetProperty(ref _saleFormViewModel, value);
    }

    /// <summary>
    /// ViewModel pour la gestion des ordonnances.
    /// </summary>
    public CustomerPrescriptionsViewModel? PrescriptionsViewModel
    {
        get => _prescriptionsViewModel;
        set => SetProperty(ref _prescriptionsViewModel, value);
    }

    /// <summary>
    /// ViewModel pour l'affichage des infos personnelles.
    /// </summary>
    public CustomerInfoViewModel? CustomerInfoViewModel
    {
        get => _customerInfoViewModel;
        set => SetProperty(ref _customerInfoViewModel, value);
    }

    /// <summary>
    /// ViewModel pour l'historique d'achats.
    /// </summary>
    public CustomerPurchaseHistoryViewModel? PurchaseHistoryViewModel
    {
        get => _purchaseHistoryViewModel;
        set => SetProperty(ref _purchaseHistoryViewModel, value);
    }

    /// <summary>
    /// Commande pour revenir à la liste des clients.
    /// </summary>
    public ICommand BackCommand { get; }

    /// <summary>
    /// Événement déclenché quand l'utilisateur revient à la liste.
    /// </summary>
    public event EventHandler? BackRequested;

    public CustomerDetailViewModel(
        ICustomerRepository customerRepository, 
        IUnitOfWork unitOfWork, 
        IOrderRepository orderRepository, 
        IPrescriptionRepository prescriptionRepository,
        IProductRepository productRepository,
        ISaleRepository saleRepository,
        IStockMovementRepository stockMovementRepository)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));

        BackCommand = new RelayCommand(ExecuteBack);
        
        Title = "Fiche Client";
    }

    /// <summary>
    /// Initialise la fiche avec un client spécifique.
    /// </summary>
    public async Task InitializeAsync(Customer customer)
    {
        Customer = customer;
        Title = $"Fiche - {customer.FirstName} {customer.LastName}";

        // Initialiser le ViewModel de commande/vente avec les repositories nécessaires
        SaleFormViewModel = new SaleFormViewModel(_saleRepository, _orderRepository, _productRepository, _prescriptionRepository, _stockMovementRepository, _unitOfWork)
        {
            Title = "Nouvelle Vente"
        };
        
        // Écouter l'événement de sauvegarde de la commande/vente pour rafraîchir l'historique
        SaleFormViewModel.OrderSaved += OnOrderSaved;
        
        // Charger les produits compatibles avec l'ordonnance du client
        await SaleFormViewModel.InitializeForCustomerAsync(customer.CustomerId);

        PrescriptionsViewModel = new CustomerPrescriptionsViewModel(_prescriptionRepository, _unitOfWork);
        await PrescriptionsViewModel.InitializeAsync(customer.CustomerId);

        CustomerInfoViewModel = new CustomerInfoViewModel(customer, _saleRepository);
        await CustomerInfoViewModel.LoadSalesAsync();

        PurchaseHistoryViewModel = new CustomerPurchaseHistoryViewModel(_saleRepository);
        await PurchaseHistoryViewModel.LoadAsync(customer.CustomerId);

        SelectedTabIndex = 0; // Commencer par l'onglet Vente
    }

    /// <summary>
    /// Gère la sauvegarde d'une commande/vente pour rafraîchir l'historique.
    /// </summary>
    private async void OnOrderSaved(object? sender, Sale sale)
    {
        // Rafraîchir l'historique d'achats
        if (PurchaseHistoryViewModel != null)
        {
            await PurchaseHistoryViewModel.LoadAsync(Customer?.CustomerId ?? 0);
        }
        
        // Rafraîchir les infos du client (nombre de ventes, etc.)
        if (CustomerInfoViewModel != null)
        {
            await CustomerInfoViewModel.LoadSalesAsync();
        }
    }

    private void ExecuteBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
