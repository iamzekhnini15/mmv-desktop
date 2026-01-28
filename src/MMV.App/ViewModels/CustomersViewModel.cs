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
    private bool _isInEditMode;
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

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
        set => SetProperty(ref _isInEditMode, value);
    }

    /// <summary>
    /// Initialise le ViewModel avec injection de dépendances.
    /// </summary>
    public CustomersViewModel(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
    {
        System.Diagnostics.Debug.WriteLine("[CustomersViewModel] Constructor called");
        _customerRepository = customerRepository;
        _unitOfWork = unitOfWork;
        
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
        IsInEditMode = true;
        Console.WriteLine($">>> IsInEditMode set to: {IsInEditMode}");
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
        IsInEditMode = true;
    }

    /// <summary>
    /// Gère l'affichage des détails d'un client.
    /// </summary>
    private void OnViewCustomerDetailsRequested(object? sender, Customer customer)
    {
        System.Diagnostics.Debug.WriteLine($"[CustomersViewModel] OnViewCustomerDetailsRequested called for customer {customer.FirstName} {customer.LastName}");
        // Afficher le formulaire
        CustomerFormViewModel = new CustomerFormViewModel(_customerRepository, _unitOfWork);
        IsInEditMode = true;
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
