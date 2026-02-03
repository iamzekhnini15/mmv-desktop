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

    public CustomerDetailViewModel(ICustomerRepository customerRepository, IUnitOfWork unitOfWork, IOrderRepository orderRepository, IPrescriptionRepository prescriptionRepository)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));

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

        // Initialiser les ViewModels des onglets
        SaleFormViewModel = new SaleFormViewModel(null, null)
        {
            CustomerId = customer.CustomerId,
            Title = "Nouvelle Vente"
        };

        PrescriptionsViewModel = new CustomerPrescriptionsViewModel(_prescriptionRepository, _unitOfWork);
        await PrescriptionsViewModel.InitializeAsync(customer.CustomerId);

        CustomerInfoViewModel = new CustomerInfoViewModel(customer);

        PurchaseHistoryViewModel = new CustomerPurchaseHistoryViewModel(_orderRepository);
        await PurchaseHistoryViewModel.LoadAsync(customer.CustomerId);

        SelectedTabIndex = 0; // Commencer par l'onglet Vente
    }

    private void ExecuteBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
