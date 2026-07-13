using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Application.UseCases.Customers.SetCustomerArchived;
using MMV.Domain.Exceptions;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des clients.
/// </summary>
public class CustomersListViewModel : BaseViewModel
{
    /// <summary>Titre du dialogue de confirmation de suppression (convention du dépôt).</summary>
    public const string DeleteConfirmationTitle = "Confirmation de suppression";

    /// <summary>
    /// Message de confirmation avant suppression <b>physique</b>. La suppression est réservée aux clients sans
    /// historique (P3-2B) : elle n'est pas le moyen normal de retirer un client de l'exploitation — c'est
    /// l'archivage qui l'est.
    /// </summary>
    public const string DeleteConfirmationMessage =
        "Supprimer définitivement ce client sans historique ? Cette action est irréversible.";

    /// <summary>Message affiché quand le client visé n'existe plus (supprimé depuis un autre poste).</summary>
    public const string CustomerNotFoundMessage = "Le client à supprimer est introuvable.";

    /// <summary>Message affiché quand le client à archiver/réactiver n'existe plus.</summary>
    public const string ArchivedCustomerNotFoundMessage = "Le client visé est introuvable.";

    private readonly IListCustomersUseCase _listCustomersUseCase;
    private readonly IDeleteCustomerUseCase _deleteCustomerUseCase;
    private readonly ISetCustomerArchivedUseCase _setCustomerArchivedUseCase;
    private readonly IDialogService _dialogService;

    private ObservableCollection<CustomerListItemDto> _customers;
    private ObservableCollection<CustomerListItemDto> _filteredCustomers;
    private CustomerListItemDto? _selectedCustomer;
    private string _searchText = string.Empty;
    private bool _includeArchived;
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
                RaiseItemCommandsCanExecuteChanged();
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
    /// Inclure les clients archivés dans la liste (P3-2C). <c>false</c> par défaut : la vue ne montre que les
    /// clients actifs. Le filtre est appliqué <b>en base</b> par <see cref="ListCustomersQuery.IncludeArchived"/> —
    /// la collection locale n'est jamais la source de vérité.
    /// </summary>
    public bool IncludeArchived
    {
        get => _includeArchived;
        set
        {
            if (SetProperty(ref _includeArchived, value))
            {
                _ = LoadCustomersAsync();
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
    public ICommand ArchiveCommand { get; }
    public ICommand ReactivateCommand { get; }
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

    public CustomersListViewModel(
        IListCustomersUseCase listCustomersUseCase,
        IDeleteCustomerUseCase deleteCustomerUseCase,
        ISetCustomerArchivedUseCase setCustomerArchivedUseCase,
        IDialogService dialogService)
    {
        System.Diagnostics.Debug.WriteLine("[CustomersListViewModel] Constructor called");
        // P2D-7C : ICustomerRepository remplacé par IListCustomersUseCase (lectures d'affichage, LoadCustomersAsync).
        // La persistance (suppression) reste déléguée à IDeleteCustomerUseCase de la couche Application : plus aucun
        // IUnitOfWork ni SaveChangesAsync ici.
        // P3-2C : archivage/réactivation délégués à ISetCustomerArchivedUseCase (aucun repository ici non plus), et
        // confirmation/erreurs portées par IDialogService (mécanisme de dialogue existant du dépôt).
        _listCustomersUseCase = listCustomersUseCase ?? throw new ArgumentNullException(nameof(listCustomersUseCase));
        _deleteCustomerUseCase = deleteCustomerUseCase ?? throw new ArgumentNullException(nameof(deleteCustomerUseCase));
        _setCustomerArchivedUseCase = setCustomerArchivedUseCase ?? throw new ArgumentNullException(nameof(setCustomerArchivedUseCase));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        _customers = new ObservableCollection<CustomerListItemDto>();
        _filteredCustomers = new ObservableCollection<CustomerListItemDto>();

        Title = "Gestion des Clients";

        // Initialiser les commandes
        // P3-2C : les commandes portant sur un client acceptent le client visé en paramètre (bouton de ligne) et
        // retombent sur SelectedCustomer quand aucun paramètre n'est fourni.
        CreateCommand = new RelayCommand(ExecuteCreate);
        EditCommand = new RelayCommand(ExecuteEdit, CanEdit);
        DeleteCommand = new RelayCommand<CustomerListItemDto>(customer => _ = ExecuteDeleteAsync(Target(customer)), CanDelete);
        ArchiveCommand = new RelayCommand<CustomerListItemDto>(customer => _ = ExecuteSetArchivedAsync(Target(customer), isArchived: true), CanArchive);
        ReactivateCommand = new RelayCommand<CustomerListItemDto>(customer => _ = ExecuteSetArchivedAsync(Target(customer), isArchived: false), CanReactivate);
        RefreshCommand = new RelayCommand(() => { _ = LoadCustomersAsync(); });
        ViewDetailsCommand = new RelayCommand(ExecuteViewDetails, CanViewDetails);
        NextPageCommand = new RelayCommand(ExecuteNextPage, CanGoNextPage);
        PreviousPageCommand = new RelayCommand(ExecutePreviousPage, CanGoPreviousPage);

        System.Diagnostics.Debug.WriteLine("[CustomersListViewModel] About to load customers...");
        // Charger les clients au démarrage
        _ = LoadCustomersAsync();
    }

    /// <summary>
    /// Client réellement visé par une commande : celui passé en paramètre (bouton de ligne), sinon la sélection.
    /// </summary>
    private CustomerListItemDto? Target(CustomerListItemDto? parameter) => parameter ?? SelectedCustomer;

    /// <summary>
    /// Charge la liste des clients depuis la base de données.
    /// </summary>
    public async Task LoadCustomersAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // P3-2C : le filtre actifs/archivés est poussé jusqu'au use case (donc jusqu'à la base) ; la liste
            // locale n'est jamais filtrée après coup sur IsArchived.
            var allCustomers = await _listCustomersUseCase.ExecuteAsync(
                new ListCustomersQuery { IncludeArchived = IncludeArchived });
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
    /// Recharge la liste depuis le use case de lecture puis restaure la sélection si le client reste visible.
    /// </summary>
    /// <remarks>
    /// Multi-poste (ADR-PROD-DB-001) : après toute mutation (suppression, refus, archivage, réactivation), l'état
    /// affiché est reconstruit depuis la source partagée. Un autre poste a pu modifier le client entre-temps, donc
    /// une mutation locale de la collection (<c>Remove</c>) mentirait sur l'état réel.
    /// </remarks>
    private async Task ReloadPreservingSelectionAsync(long? customerIdToReselect)
    {
        await LoadCustomersAsync();

        SelectedCustomer = customerIdToReselect is null
            ? null
            : Customers.FirstOrDefault(c => c.CustomerId == customerIdToReselect.Value);
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
    /// Exécute la suppression physique du client visé, après confirmation explicite.
    /// </summary>
    /// <remarks>
    /// La suppression n'est autorisée par la couche Application que pour un client <b>sans historique</b> ; un refus
    /// métier remonte en <see cref="BusinessRuleException"/> (P3-2B). Les <c>CanExecute</c> ne sont qu'un confort
    /// d'écran : l'autorité reste le use case, et la base (clés étrangères <c>Restrict</c>) le dernier rempart.
    /// </remarks>
    public async Task ExecuteDeleteAsync(CustomerListItemDto? customer)
    {
        if (customer == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            DeleteConfirmationTitle,
            DeleteConfirmationMessage);

        // Annulation : aucun appel au use case, aucun changement d'état local.
        if (!confirmed) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // P2C-3 : la suppression (DeleteAsync + SaveChangesAsync) est portée par la couche Application.
            // La VM construit la commande à partir de son état et délègue ; elle conserve la mise à jour d'écran.
            var result = await _deleteCustomerUseCase.ExecuteAsync(
                new DeleteCustomerCommand { CustomerId = customer.CustomerId });

            if (!result.CustomerFound)
            {
                // Le client a pu être supprimé depuis un autre poste : on repart de l'état partagé.
                await ReloadPreservingSelectionAsync(null);
                ErrorMessage = CustomerNotFoundMessage;
                return;
            }

            await ReloadPreservingSelectionAsync(null);
        }
        catch (BusinessRuleException ex)
        {
            // Refus métier (client porteur d'historique) : message métier, jamais de type ni de pile technique.
            // Le message porté par l'exception est déjà celui destiné à l'utilisateur
            // (DeleteCustomerUseCase.CustomerHasHistoryMessage) et indique l'archivage comme alternative : on
            // l'affiche tel quel plutôt que de le comparer à une chaîne, ce qui resterait fragile.
            // Le client N'EST PAS retiré de la collection : la liste est rechargée depuis la source.
            await ReloadPreservingSelectionAsync(customer.CustomerId);
            ErrorMessage = ex.Message;
            await _dialogService.ShowErrorAsync(DeleteConfirmationTitle, ex.Message);
        }
        catch (Exception ex)
        {
            // Erreurs inattendues (dont la violation de contrainte en course multi-poste, cf. rapport P3-2B §12) :
            // traitement générique, distinct du refus métier.
            ErrorMessage = $"Erreur lors de la suppression du client : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Archive (<paramref name="isArchived"/> = <c>true</c>) ou réactive (<c>false</c>) le client visé.
    /// </summary>
    /// <remarks>
    /// L'état demandé est <b>absolu</b>, pas un basculement : l'intention provient de l'état actuellement affiché,
    /// et le use case est idempotent (P3-2B). Deux postes qui archivent le même client convergent.
    /// </remarks>
    public async Task ExecuteSetArchivedAsync(CustomerListItemDto? customer, bool isArchived)
    {
        if (customer == null) return;

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _setCustomerArchivedUseCase.ExecuteAsync(
                new SetCustomerArchivedCommand
                {
                    CustomerId = customer.CustomerId,
                    IsArchived = isArchived,
                });

            if (!result.CustomerFound)
            {
                await ReloadPreservingSelectionAsync(null);
                ErrorMessage = ArchivedCustomerNotFoundMessage;
                return;
            }

            // Rechargement depuis la base : un client archivé disparaît de la vue « actifs seulement », la sélection
            // n'est restaurée que s'il y reste visible.
            await ReloadPreservingSelectionAsync(customer.CustomerId);
        }
        catch (Exception ex)
        {
            ErrorMessage = isArchived
                ? $"Erreur lors de l'archivage du client : {ex.Message}"
                : $"Erreur lors de la réactivation du client : {ex.Message}";
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
    /// Notifie les commandes dépendant du client visé.
    /// </summary>
    private void RaiseItemCommandsCanExecuteChanged()
    {
        ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
        ((RelayCommand<CustomerListItemDto>)DeleteCommand).RaiseCanExecuteChanged();
        ((RelayCommand<CustomerListItemDto>)ArchiveCommand).RaiseCanExecuteChanged();
        ((RelayCommand<CustomerListItemDto>)ReactivateCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ViewDetailsCommand).RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Consultation : possible aussi pour un client archivé (lecture seule de son historique).
    /// </summary>
    private bool CanViewDetails() => SelectedCustomer != null && !IsLoading;

    /// <summary>
    /// Édition : refusée pour un client archivé — il faut d'abord le réactiver.
    /// </summary>
    private bool CanEdit() => SelectedCustomer != null && !IsLoading && !SelectedCustomer.IsArchived;

    /// <summary>
    /// Suppression : proposée seulement pour un client actif. L'autorité reste le use case, qui refuse tout client
    /// porteur d'historique — cette garde n'est qu'un confort d'écran.
    /// </summary>
    private bool CanDelete(CustomerListItemDto? customer)
    {
        var target = Target(customer);
        return target != null && !IsLoading && !target.IsArchived;
    }

    /// <summary>
    /// Archiver : client actif visé.
    /// </summary>
    private bool CanArchive(CustomerListItemDto? customer)
    {
        var target = Target(customer);
        return target != null && !IsLoading && !target.IsArchived;
    }

    /// <summary>
    /// Réactiver : client archivé visé.
    /// </summary>
    private bool CanReactivate(CustomerListItemDto? customer)
    {
        var target = Target(customer);
        return target != null && !IsLoading && target.IsArchived;
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
