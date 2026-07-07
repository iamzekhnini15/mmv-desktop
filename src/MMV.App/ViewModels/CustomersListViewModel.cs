using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Application.UseCases.Customers.ListCustomers;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des clients.
/// </summary>
public class CustomersListViewModel : BaseViewModel
{
    private readonly IListCustomersUseCase _listCustomersUseCase;
    private readonly IDeleteCustomerUseCase _deleteCustomerUseCase;

    private ObservableCollection<CustomerListItemDto> _customers;
    private ObservableCollection<CustomerListItemDto> _filteredCustomers;
    private CustomerListItemDto? _selectedCustomer;
    private string _searchText = string.Empty;
    private int _currentPage = 1;
    private int _pageSize = 20;
    private int _totalCustomers;

    /// <summary>
    /// Liste complète des clients.
    /// </summary>
    public ObservableCollection<CustomerListItemDto> Customers
    {
        get => _customers;
        set => SetProperty(ref _customers, value);
    }

    /// <summary>
    /// Liste filtrée des clients (après recherche).
    /// </summary>
    public ObservableCollection<CustomerListItemDto> FilteredCustomers
    {
        get => _filteredCustomers;
        set => SetProperty(ref _filteredCustomers, value);
    }

    /// <summary>
    /// Client sélectionné dans la liste.
    /// </summary>
    public CustomerListItemDto? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (SetProperty(ref _selectedCustomer, value))
            {
                ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ViewDetailsCommand).RaiseCanExecuteChanged();
            }
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
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>
    /// Page actuelle (pagination).
    /// </summary>
    public int CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    /// <summary>
    /// Taille de la page (nombre d'éléments par page).
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (SetProperty(ref _pageSize, value))
            {
                _ = LoadCustomersAsync();
            }
        }
    }

    /// <summary>
    /// Nombre total de clients.
    /// </summary>
    public int TotalCustomers
    {
        get => _totalCustomers;
        set => SetProperty(ref _totalCustomers, value);
    }

    /// <summary>
    /// Nombre total de pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCustomers / PageSize);

    // Commandes
    public ICommand CreateCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ViewDetailsCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }

    /// <summary>
    /// Événement déclenché quand on veut créer un nouveau client.
    /// </summary>
    public event EventHandler? CreateCustomerRequested;

    /// <summary>
    /// Événement déclenché quand on veut éditer un client.
    /// </summary>
    public event EventHandler<CustomerListItemDto>? EditCustomerRequested;

    /// <summary>
    /// Événement déclenché quand on veut voir les détails d'un client.
    /// </summary>
    public event EventHandler<CustomerListItemDto>? ViewCustomerDetailsRequested;

    public CustomersListViewModel(IListCustomersUseCase listCustomersUseCase, IDeleteCustomerUseCase deleteCustomerUseCase)
    {
        System.Diagnostics.Debug.WriteLine("[CustomersListViewModel] Constructor called");
        // P2D-7C : ICustomerRepository remplacé par IListCustomersUseCase (lectures d'affichage, LoadCustomersAsync).
        // La persistance (suppression) reste déléguée à IDeleteCustomerUseCase de la couche Application : plus aucun
        // IUnitOfWork ni SaveChangesAsync ici.
        _listCustomersUseCase = listCustomersUseCase ?? throw new ArgumentNullException(nameof(listCustomersUseCase));
        _deleteCustomerUseCase = deleteCustomerUseCase ?? throw new ArgumentNullException(nameof(deleteCustomerUseCase));

        _customers = new ObservableCollection<CustomerListItemDto>();
        _filteredCustomers = new ObservableCollection<CustomerListItemDto>();
        
        Title = "Gestion des Clients";
        
        // Initialiser les commandes
        CreateCommand = new RelayCommand(ExecuteCreate);
        EditCommand = new RelayCommand(ExecuteEdit, CanEditOrDelete);
        DeleteCommand = new RelayCommand(ExecuteDelete, CanEditOrDelete);
        RefreshCommand = new RelayCommand(() => { _ = LoadCustomersAsync(); });
        ViewDetailsCommand = new RelayCommand(ExecuteViewDetails, CanEditOrDelete);
        NextPageCommand = new RelayCommand(ExecuteNextPage, CanGoNextPage);
        PreviousPageCommand = new RelayCommand(ExecutePreviousPage, CanGoPreviousPage);
        
        System.Diagnostics.Debug.WriteLine("[CustomersListViewModel] About to load customers...");
        // Charger les clients au démarrage
        _ = LoadCustomersAsync();
    }

    /// <summary>
    /// Charge la liste des clients depuis la base de données.
    /// </summary>
    public async Task LoadCustomersAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Récupérer tous les clients
            var allCustomers = await _listCustomersUseCase.ExecuteAsync(new ListCustomersQuery());
            System.Diagnostics.Debug.WriteLine($"[CustomersListViewModel] Loaded {allCustomers.Count()} customers from database");

            Customers = new ObservableCollection<CustomerListItemDto>(allCustomers);
            TotalCustomers = Customers.Count;
            System.Diagnostics.Debug.WriteLine($"[CustomersListViewModel] Total customers: {TotalCustomers}");
            
            // Appliquer le filtre
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des clients : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Applique le filtre de recherche.
    /// </summary>
    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            FilteredCustomers = new ObservableCollection<CustomerListItemDto>(Customers);
            System.Diagnostics.Debug.WriteLine($"[CustomersListViewModel] ApplyFilter: showing all {FilteredCustomers.Count} customers");
        }
        else
        {
            var searchLower = SearchText.ToLower();
            var filtered = Customers.Where(c =>
                c.FirstName.ToLower().Contains(searchLower) ||
                c.LastName.ToLower().Contains(searchLower) ||
                (!string.IsNullOrEmpty(c.Email) && c.Email.ToLower().Contains(searchLower)) ||
                (!string.IsNullOrEmpty(c.Phone) && c.Phone.Contains(SearchText))
            ).ToList();

            FilteredCustomers = new ObservableCollection<CustomerListItemDto>(filtered);
            System.Diagnostics.Debug.WriteLine($"[CustomersListViewModel] ApplyFilter with search '{SearchText}': found {FilteredCustomers.Count} customers");
        }
    }

    /// <summary>
    /// Exécute la création d'un nouveau client.
    /// </summary>
    private void ExecuteCreate()
    {
        Console.WriteLine(">>> BOUTON NOUVEAU CLIQUE <<<");
        Console.WriteLine($">>> CreateCustomerRequested subscribers: {CreateCustomerRequested?.GetInvocationList().Length ?? 0}");
        CreateCustomerRequested?.Invoke(this, EventArgs.Empty);
        Console.WriteLine(">>> Event invoked");
    }

    /// <summary>
    /// Exécute l'édition du client sélectionné.
    /// </summary>
    private void ExecuteEdit()
    {
        if (SelectedCustomer != null)
        {
            EditCustomerRequested?.Invoke(this, SelectedCustomer);
        }
    }

    /// <summary>
    /// Exécute la suppression du client sélectionné.
    /// </summary>
    private async void ExecuteDelete()
    {
        if (SelectedCustomer == null) return;

        var customerToDelete = SelectedCustomer;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // P2C-3 : la suppression (DeleteAsync + SaveChangesAsync) est portée par la couche Application.
            // La VM construit la commande à partir de son état et délègue ; elle conserve la mise à jour d'écran.
            var result = await _deleteCustomerUseCase.ExecuteAsync(
                new DeleteCustomerCommand { CustomerId = customerToDelete.CustomerId });

            if (!result.CustomerFound)
            {
                ErrorMessage = "Le client à supprimer est introuvable.";
                return;
            }

            // Retirer de la liste
            Customers.Remove(customerToDelete);
            ApplyFilter();

            SelectedCustomer = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la suppression du client : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Exécute l'affichage des détails du client.
    /// </summary>
    private void ExecuteViewDetails()
    {
        if (SelectedCustomer != null)
        {
            ViewCustomerDetailsRequested?.Invoke(this, SelectedCustomer);
        }
    }

    /// <summary>
    /// Passe à la page suivante.
    /// </summary>
    private void ExecuteNextPage()
    {
        if (CanGoNextPage())
        {
            CurrentPage++;
            _ = LoadCustomersAsync();
        }
    }

    /// <summary>
    /// Revient à la page précédente.
    /// </summary>
    private void ExecutePreviousPage()
    {
        if (CanGoPreviousPage())
        {
            CurrentPage--;
            _ = LoadCustomersAsync();
        }
    }

    /// <summary>
    /// Vérifie si on peut éditer ou supprimer.
    /// </summary>
    private bool CanEditOrDelete()
    {
        return SelectedCustomer != null && !IsLoading;
    }

    /// <summary>
    /// Vérifie si on peut aller à la page suivante.
    /// </summary>
    private bool CanGoNextPage()
    {
        return CurrentPage < TotalPages;
    }

    /// <summary>
    /// Vérifie si on peut revenir à la page précédente.
    /// </summary>
    private bool CanGoPreviousPage()
    {
        return CurrentPage > 1;
    }
}
