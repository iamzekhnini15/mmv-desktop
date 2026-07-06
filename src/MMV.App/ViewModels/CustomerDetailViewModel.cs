using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'un client avec onglets.
/// </summary>
public class CustomerDetailViewModel : BaseViewModel
{
    // P2D-5 : ICustomerRepository (dépendance morte) et ISaleRepository ont été retirés. Les lectures d'affichage
    // (historique d'achats, ordonnances) passent désormais par des query use cases Application. IPrescriptionRepository
    // et IProductRepository subsistent uniquement pour construire SaleFormViewModel (formulaire de vente — lectures de
    // référence à migrer en P2D-6 / Ventes) ; ils ne sont plus utilisés directement pour les lectures de la fiche.
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IProductRepository _productRepository;
    private readonly IRegisterSaleUseCase _registerSaleUseCase;
    private readonly IGetCustomerPurchaseHistoryUseCase _getPurchaseHistoryUseCase;
    private readonly IListPrescriptionsByCustomerUseCase _listPrescriptionsUseCase;
    // P2C-4 : use cases d'écriture des ordonnances, transmis à CustomerPrescriptionsViewModel.
    private readonly ICreatePrescriptionUseCase _createPrescriptionUseCase;
    private readonly IUpdatePrescriptionUseCase _updatePrescriptionUseCase;
    private readonly IDeletePrescriptionUseCase _deletePrescriptionUseCase;

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

    // P2C-GLOBAL : le paramètre IUnitOfWork (historiquement injecté mais jamais stocké ni utilisé — dépendance
    // morte) a été supprimé. La fiche détail ne fait aucune écriture directe : les écritures d'ordonnance passent
    // par les use cases transmis à CustomerPrescriptionsViewModel.
    // P2D-5 : ICustomerRepository et ISaleRepository retirés ; les lectures passent par IGetCustomerPurchaseHistoryUseCase
    // et IListPrescriptionsByCustomerUseCase. IPrescriptionRepository / IProductRepository conservés pour SaleFormViewModel.
    public CustomerDetailViewModel(
        IPrescriptionRepository prescriptionRepository,
        IProductRepository productRepository,
        IRegisterSaleUseCase registerSaleUseCase,
        IGetCustomerPurchaseHistoryUseCase getPurchaseHistoryUseCase,
        IListPrescriptionsByCustomerUseCase listPrescriptionsUseCase,
        ICreatePrescriptionUseCase createPrescriptionUseCase,
        IUpdatePrescriptionUseCase updatePrescriptionUseCase,
        IDeletePrescriptionUseCase deletePrescriptionUseCase)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        // Use case de sauvegarde de vente obligatoire (P2B-2C), transmis à SaleFormViewModel.
        _registerSaleUseCase = registerSaleUseCase ?? throw new ArgumentNullException(nameof(registerSaleUseCase));
        // P2D-5 : query use cases de lecture (historique d'achats + ordonnances), transmis aux sous-ViewModels.
        _getPurchaseHistoryUseCase = getPurchaseHistoryUseCase ?? throw new ArgumentNullException(nameof(getPurchaseHistoryUseCase));
        _listPrescriptionsUseCase = listPrescriptionsUseCase ?? throw new ArgumentNullException(nameof(listPrescriptionsUseCase));
        // P2C-4 : use cases d'écriture des ordonnances, transmis à CustomerPrescriptionsViewModel.
        _createPrescriptionUseCase = createPrescriptionUseCase ?? throw new ArgumentNullException(nameof(createPrescriptionUseCase));
        _updatePrescriptionUseCase = updatePrescriptionUseCase ?? throw new ArgumentNullException(nameof(updatePrescriptionUseCase));
        _deletePrescriptionUseCase = deletePrescriptionUseCase ?? throw new ArgumentNullException(nameof(deletePrescriptionUseCase));

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

        // Initialiser le ViewModel de vente : repositories nécessaires au chargement d'écran (produits +
        // ordonnance) et use case applicatif pour la sauvegarde (P2B-2C). Plus aucune primitive de
        // transaction / numérotation / stock transmise à la ViewModel.
        SaleFormViewModel = new SaleFormViewModel(_productRepository, _prescriptionRepository, _registerSaleUseCase)
        {
            Title = "Nouvelle Vente"
        };
        
        // Écouter l'événement de sauvegarde de la commande/vente pour rafraîchir l'historique
        SaleFormViewModel.OrderSaved += OnOrderSaved;
        
        // Charger les produits compatibles avec l'ordonnance du client
        await SaleFormViewModel.InitializeForCustomerAsync(customer.CustomerId);

        PrescriptionsViewModel = new CustomerPrescriptionsViewModel(
            _listPrescriptionsUseCase,
            _createPrescriptionUseCase,
            _updatePrescriptionUseCase,
            _deletePrescriptionUseCase);
        await PrescriptionsViewModel.InitializeAsync(customer.CustomerId);

        CustomerInfoViewModel = new CustomerInfoViewModel(customer, _getPurchaseHistoryUseCase);
        await CustomerInfoViewModel.LoadSalesAsync();

        PurchaseHistoryViewModel = new CustomerPurchaseHistoryViewModel(_getPurchaseHistoryUseCase);
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
