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
    
    private Customer? _customer;
    private int _selectedTabIndex = 0;
    private SaleFormViewModel? _saleFormViewModel;
    private PrescriptionFormViewModel? _prescriptionFormViewModel;
    private CustomerInfoViewModel? _customerInfoViewModel;

    /// <summary>
    /// Client en cours de consultation.
    /// </summary>
    public Customer? Customer
    {
        get => _customer;
        set => SetProperty(ref _customer, value);
    }

    /// <summary>
    /// Index de l'onglet sélectionné (0: Vente, 1: Ordonnance, 2: Infos).
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
    /// ViewModel pour le formulaire d'ordonnance.
    /// </summary>
    public PrescriptionFormViewModel? PrescriptionFormViewModel
    {
        get => _prescriptionFormViewModel;
        set => SetProperty(ref _prescriptionFormViewModel, value);
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
    /// Commande pour revenir à la liste des clients.
    /// </summary>
    public ICommand BackCommand { get; }

    /// <summary>
    /// Événement déclenché quand l'utilisateur revient à la liste.
    /// </summary>
    public event EventHandler? BackRequested;

    public CustomerDetailViewModel(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

        BackCommand = new RelayCommand(ExecuteBack);
        
        Title = "Fiche Client";
    }

    /// <summary>
    /// Initialise la fiche avec un client spécifique.
    /// </summary>
    public void Initialize(Customer customer)
    {
        Customer = customer;
        Title = $"Fiche - {customer.FirstName} {customer.LastName}";

        // Initialiser les ViewModels des onglets
        SaleFormViewModel = new SaleFormViewModel(null, null)
        {
            CustomerId = customer.CustomerId,
            Title = "Nouvelle Vente"
        };

        PrescriptionFormViewModel = new PrescriptionFormViewModel(null, null)
        {
            CustomerId = customer.CustomerId,
            Title = "Nouvelle Ordonnance"
        };

        CustomerInfoViewModel = new CustomerInfoViewModel(customer);

        SelectedTabIndex = 0; // Commencer par l'onglet Vente
    }

    private void ExecuteBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }
}
