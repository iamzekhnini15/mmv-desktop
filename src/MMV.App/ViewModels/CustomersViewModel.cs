using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Clients.
/// Contient le CustomersListViewModel pour afficher la liste des clients.
/// </summary>
public class CustomersViewModel : BaseViewModel
{
    private CustomersListViewModel _customersListViewModel;
    private CustomerFormViewModel? _customerFormViewModel;
    private CustomerDetailViewModel? _customerDetailViewModel;
    private bool _isInEditMode;
    private bool _isCreatingNew;
    private bool _isShowingDetail;
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderRepository _orderRepository;
    private ICommand? _viewDetailCommand;

    /// <summary>
    /// ViewModel pour la liste des clients.
    /// </summary>
    public CustomersListViewModel CustomersListViewModel
    {
        get => _customersListViewModel;
        set => SetProperty(ref _customersListViewModel, value);
    }

    /// <summary>
    /// ViewModel du formulaire client.
    /// </summary>
    public CustomerFormViewModel? CustomerFormViewModel
    {
        get => _customerFormViewModel;
        set => SetProperty(ref _customerFormViewModel, value);
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
    /// Indique si on est en train de créer un nouveau client.
    /// </summary>
    public bool IsCreatingNew
    {
        get => _isCreatingNew;
        set => SetProperty(ref _isCreatingNew, value);
    }

    /// <summary>
    /// ViewModel pour la fiche détaillée du client.
    /// </summary>
    public CustomerDetailViewModel? CustomerDetailViewModel
    {
        get => _customerDetailViewModel;
        set => SetProperty(ref _customerDetailViewModel, value);
    }

    /// <summary>
    /// Indique si on affiche la fiche détaillée du client.
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
    public bool ShowList => !IsInEditMode && !IsShowingDetail;

    /// <summary>
    /// Commande pour afficher la fiche détaillée du client.
    /// </summary>
    public ICommand ViewDetailCommand
    {
        get => _viewDetailCommand ??= new RelayCommand<Customer>(ExecuteViewDetail, CanViewDetail);
    }

    /// <summary>
    /// Initialise le ViewModel avec injection de dépendances.
    /// </summary>
    public CustomersViewModel(ICustomerRepository customerRepository, IUnitOfWork unitOfWork, IOrderRepository orderRepository)
    {
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] Constructor called");
        _customerRepository = customerRepository;
        _unitOfWork = unitOfWork;
        _orderRepository = orderRepository;
        
        Title = "Clients";
        
        // Initialiser le ViewModel de la liste avec les bonnes dépendances
        _customersListViewModel = new CustomersListViewModel(customerRepository, unitOfWork);
        
        // Écouter les événements du ViewModel de la liste
        _customersListViewModel.CreateCustomerRequested += OnCreateCustomerRequested;
        _customersListViewModel.EditCustomerRequested += OnEditCustomerRequested;
        _customersListViewModel.ViewCustomerDetailsRequested += OnViewCustomerDetailsRequested;
        
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] Event handlers registered");
    }

    /// <summary>
    /// Gère la création d'un nouveau client.
    /// </summary>
    private void OnCreateCustomerRequested(object? sender, EventArgs e)
    {
        Console.WriteLine(">>> CustomersViewModel: OnCreateCustomerRequested RECEIVED <<<");
        // Créer une nouvelle instance du formulaire vide
        CustomerFormViewModel = new CustomerFormViewModel(_customerRepository, _unitOfWork);
        CustomerFormViewModel.InitializeForCreate(); // Initialize for create mode
        
        // S'abonner aux événements pour fermer le formulaire et rafraîchir la liste
        CustomerFormViewModel.CustomerSaved += OnCustomerFormSaved;
        CustomerFormViewModel.Cancelled += OnCustomerFormCancelled;
        
        IsInEditMode = true;
        IsCreatingNew = true; // Mark as creating new customer
        
        Console.WriteLine($">>> IsInEditMode set to: {IsInEditMode}");
        Console.WriteLine($">>> IsCreatingNew set to: {IsCreatingNew}");
        Console.WriteLine($">>> CustomerFormViewModel created: {CustomerFormViewModel != null}");
    }

    /// <summary>
    /// Gère l'édition d'un client.
    /// </summary>
    private void OnEditCustomerRequested(object? sender, Customer customer)
    {
        System.Diagnostics.Debug.WriteLine($"[CustomersViewModel] OnEditCustomerRequested called for customer {customer.FirstName} {customer.LastName}");
        // Créer une instance du formulaire avec les données du client
        CustomerFormViewModel = new CustomerFormViewModel(_customerRepository, _unitOfWork);
        CustomerFormViewModel.InitializeForEdit(customer); // Initialize with customer data
        CustomerFormViewModel.CustomerSaved += OnCustomerFormSaved;
        CustomerFormViewModel.Cancelled += OnCustomerFormCancelled;
        
        IsInEditMode = true;
        IsCreatingNew = false;
    }

    private async void OnCustomerFormSaved(object? sender, Customer customer)
    {
        System.Diagnostics.Debug.WriteLine($"[CustomersViewModel] OnCustomerFormSaved called for {customer.FirstName} {customer.LastName}");
        // Après sauvegarde par le formulaire, rafraîchir la liste et fermer le formulaire
        await CustomersListViewModel.LoadCustomersAsync();
        CloseForm();
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] Form closed and list refreshed");
    }

    private void OnCustomerFormCancelled(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] OnCustomerFormCancelled called");
        CloseForm();
    }

    /// <summary>
    /// Gère l'affichage des détails d'un client.
    /// </summary>
    private void OnViewCustomerDetailsRequested(object? sender, Customer customer)
    {
        System.Diagnostics.Debug.WriteLine($"[CustomersViewModel] OnViewCustomerDetailsRequested called for customer {customer.FirstName} {customer.LastName}");
        // Afficher la fiche détaillée
        ExecuteViewDetail(customer);
    }

    /// <summary>
    /// Exécute l'affichage de la fiche détaillée du client.
    /// </summary>
    private void ExecuteViewDetail(Customer? customer)
    {
        if (customer != null)
        {
            // Créer une nouvelle instance du CustomerDetailViewModel
            CustomerDetailViewModel = new CustomerDetailViewModel(_customerRepository, _unitOfWork, _orderRepository);
            
            // Initialiser avec le client sélectionné
            CustomerDetailViewModel.Initialize(customer);
            
            // Écouter l'événement de retour
            CustomerDetailViewModel.BackRequested += OnDetailBackRequested;
            
            // Afficher la fiche détaillée et masquer les autres vues
            IsShowingDetail = true;
            IsInEditMode = false;
            
            System.Diagnostics.Debug.WriteLine($"[CustomersViewModel] Showing detail for {customer.FirstName} {customer.LastName}");
        }
    }

    /// <summary>
    /// Vérifie si on peut afficher la fiche détaillée.
    /// </summary>
    private bool CanViewDetail(Customer? customer)
    {
        return customer != null && customer.CustomerId > 0;
    }

    /// <summary>
    /// Ferme la fiche détaillée et revient à la liste.
    /// </summary>
    public void CloseDetail()
    {
        IsShowingDetail = false;
        CustomerDetailViewModel = null;
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] Closed detail view");
    }

    /// <summary>
    /// Gère le retour depuis la fiche détaillée.
    /// </summary>
    private void OnDetailBackRequested(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] Detail back requested");
        CloseDetail();
    }

    /// <summary>
    /// Ferme le formulaire et revient à la liste.
    /// </summary>
    public void CloseForm()
    {
        IsInEditMode = false;
        CustomerFormViewModel = null;
    }
}
