using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Domain.Enums;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre principale.
/// Gère la navigation, l'état global, l'affichage utilisateur et les permissions.
/// <para>
/// P2C-GLOBAL : la génération des notifications de stock bas et le comptage des non lues sont désormais assurés par
/// <see cref="IGenerateLowStockNotificationsUseCase"/> (couche Application). Les dépendances de persistance directes
/// (<c>INotificationRepository</c>, <c>IProductRepository</c>, <c>IUnitOfWork</c>) ont été retirées.
/// </para>
/// </summary>
public class MainWindowViewModel : BaseViewModel
{
    private BaseViewModel? _currentView;
    private readonly INavigationService _navigationService;
    private readonly ISessionService _sessionService;
    private readonly IPermissionService _permissionService;
    private readonly IGenerateLowStockNotificationsUseCase? _generateLowStockNotificationsUseCase;
    private int _unreadNotificationsCount;
    private bool _isUserMenuOpen;

    /// <summary>
    /// Collection des éléments du menu de navigation.
    /// </summary>
    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    /// <summary>
    /// Vue actuellement affichée.
    /// </summary>
    public BaseViewModel? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    /// <summary>
    /// Nombre de notifications non lues.
    /// </summary>
    public int UnreadNotificationsCount
    {
        get => _unreadNotificationsCount;
        set => SetProperty(ref _unreadNotificationsCount, value);
    }

    /// <summary>
    /// Indique si le menu utilisateur est ouvert.
    /// </summary>
    public bool IsUserMenuOpen
    {
        get => _isUserMenuOpen;
        set => SetProperty(ref _isUserMenuOpen, value);
    }

    #region User display properties

    /// <summary>
    /// Nom complet de l'utilisateur connecté.
    /// </summary>
    public string CurrentUserDisplayName => _sessionService.CurrentUser != null
        ? $"{_sessionService.CurrentUser.FirstName} {_sessionService.CurrentUser.LastName}"
        : "Non connecté";

    /// <summary>
    /// Initiale de l'utilisateur pour l'avatar.
    /// </summary>
    public string CurrentUserInitial => _sessionService.CurrentUser?.FirstName?.Length > 0
        ? _sessionService.CurrentUser.FirstName[0].ToString().ToUpper()
        : "?";

    /// <summary>
    /// Rôle affiché de l'utilisateur connecté.
    /// </summary>
    public string CurrentUserRole => _sessionService.CurrentUser?.Role switch
    {
        UserRole.Admin => "Administrateur",
        UserRole.Optician => "Opticien",
        UserRole.Technician => "Technicien",
        _ => "Inconnu"
    };

    /// <summary>
    /// Indique si le module Utilisateurs est visible (ADMIN uniquement).
    /// </summary>
    public bool CanSeeUsersModule => _permissionService.CanAccessModule("Users");

    /// <summary>
    /// Indique si le module Ventes est visible.
    /// </summary>
    public bool CanSeeSalesModule => _permissionService.CanAccessModule("Sales");

    /// <summary>
    /// Indique si le module Rapports est visible.
    /// </summary>
    public bool CanSeeReportsModule => _permissionService.CanAccessModule("Reports");

    /// <summary>
    /// Indique si le module Paramètres est visible.
    /// </summary>
    public bool CanSeeSettingsModule => _permissionService.CanAccessModule("Settings");

    #endregion

    /// <summary>
    /// Commande pour naviguer vers une vue.
    /// </summary>
    public ICommand NavigateCommand { get; }

    /// <summary>
    /// Commande pour se déconnecter.
    /// </summary>
    public ICommand LogoutCommand { get; }

    /// <summary>
    /// Commande pour ouvrir le profil utilisateur.
    /// </summary>
    public ICommand OpenProfileCommand { get; }

    /// <summary>
    /// Commande pour basculer le menu utilisateur.
    /// </summary>
    public ICommand ToggleUserMenuCommand { get; }

    /// <summary>
    /// Événement déclenché lors de la déconnexion.
    /// </summary>
    public event EventHandler? LogoutRequested;

    public MainWindowViewModel(
        INavigationService navigationService,
        ISessionService sessionService,
        IPermissionService permissionService,
        IGenerateLowStockNotificationsUseCase? generateLowStockNotificationsUseCase = null)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _generateLowStockNotificationsUseCase = generateLowStockNotificationsUseCase;
        Title = "ManageMyVision";
        
        NavigateCommand = new RelayCommand<string>(ExecuteNavigate, CanNavigate);
        LogoutCommand = new RelayCommand(ExecuteLogout);
        OpenProfileCommand = new RelayCommand(() => ExecuteNavigate("UserProfile"));
        ToggleUserMenuCommand = new RelayCommand(() => IsUserMenuOpen = !IsUserMenuOpen);
        
        // Écouter l'expiration de la session
        _sessionService.SessionExpired += OnSessionExpired;

        InitializeNavigation();
        RegisterViewModels();
        LoadDashboard();
        
        // Charger le compteur de notifications
        _ = LoadUnreadNotificationsCountAsync();
    }

    /// <summary>
    /// Initialise les éléments du menu de navigation selon les permissions.
    /// </summary>
    private void InitializeNavigation()
    {
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "📊", 
            Label = "Tableau de Bord", 
            ViewName = "Dashboard" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "👥", 
            Label = "Clients", 
            ViewName = "Customers" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "📦", 
            Label = "Produits", 
            ViewName = "Products" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "📋", 
            Label = "Ordonnances", 
            ViewName = "Prescriptions" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "🏭", 
            Label = "Commandes", 
            ViewName = "Orders" 
        });

        if (_permissionService.CanAccessModule("Sales"))
        {
            NavigationItems.Add(new NavigationItem 
            { 
                Icon = "💰", 
                Label = "Ventes", 
                ViewName = "Sales" 
            });
        }

        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "📦", 
            Label = "Inventaire", 
            ViewName = "Inventory" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "🔔", 
            Label = "Notifications", 
            ViewName = "Notifications",
            HasBadge = true
        });

        if (_permissionService.CanAccessModule("Reports"))
        {
            NavigationItems.Add(new NavigationItem 
            { 
                Icon = "📈", 
                Label = "Rapports", 
                ViewName = "Reports" 
            });
        }

        if (_permissionService.CanAccessModule("Settings"))
        {
            NavigationItems.Add(new NavigationItem 
            { 
                Icon = "⚙️", 
                Label = "Paramètres", 
                ViewName = "Settings" 
            });
        }

        if (_permissionService.CanAccessModule("Users"))
        {
            NavigationItems.Add(new NavigationItem 
            { 
                Icon = "🔐", 
                Label = "Utilisateurs", 
                ViewName = "Users" 
            });
        }
    }

    /// <summary>
    /// Enregistre les ViewModels pour chaque vue.
    /// </summary>
    private void RegisterViewModels()
    {
        _navigationService.RegisterViewModel("Dashboard", typeof(DashboardViewModel));
        _navigationService.RegisterViewModel("Customers", typeof(CustomersViewModel));
        _navigationService.RegisterViewModel("Products", typeof(ProductsViewModel));
        _navigationService.RegisterViewModel("Prescriptions", typeof(PrescriptionsViewModel));
        _navigationService.RegisterViewModel("Orders", typeof(OrdersViewModel));
        _navigationService.RegisterViewModel("Sales", typeof(SalesViewModel));
        _navigationService.RegisterViewModel("Inventory", typeof(InventoryViewModel));
        _navigationService.RegisterViewModel("Notifications", typeof(NotificationsViewModel));
        _navigationService.RegisterViewModel("Reports", typeof(ReportsViewModel));
        _navigationService.RegisterViewModel("Settings", typeof(SettingsViewModel));
        _navigationService.RegisterViewModel("Users", typeof(UsersViewModel));
        _navigationService.RegisterViewModel("UserProfile", typeof(UserProfileViewModel));
    }

    /// <summary>
    /// Charge le tableau de bord au démarrage.
    /// </summary>
    private void LoadDashboard()
    {
        _navigationService.Navigate("Dashboard");
        CurrentView = _navigationService.CurrentViewModel;
    }

    /// <summary>
    /// Navigue vers une vue donnée.
    /// </summary>
    public void NavigateTo(string viewName)
    {
        ExecuteNavigate(viewName);
    }

    /// <summary>
    /// Exécute la commande de navigation.
    /// </summary>
    private void ExecuteNavigate(string? viewName)
    {
        if (string.IsNullOrEmpty(viewName)) return;
        
        // Vérifier les permissions d'accès au module
        if (!_permissionService.CanAccessModule(viewName) && viewName != "UserProfile")
        {
            ErrorMessage = "Vous n'avez pas les permissions pour accéder à ce module.";
            return;
        }

        // Reset le timer d'inactivité de la session
        _sessionService.ResetInactivityTimer();

        try
        {
            _navigationService.Navigate(viewName);
            CurrentView = _navigationService.CurrentViewModel;
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Impossible d'ouvrir le module '{viewName}' : {ex.InnerException?.Message ?? ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Navigation] Erreur pour '{viewName}': {ex}");
        }

        IsUserMenuOpen = false;
    }

    /// <summary>
    /// Vérifie si la navigation est possible.
    /// </summary>
    private bool CanNavigate(string? viewName)
    {
        return !string.IsNullOrEmpty(viewName);
    }

    /// <summary>
    /// Exécute la déconnexion.
    /// </summary>
    private void ExecuteLogout()
    {
        _sessionService.Logout();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gestionnaire d'expiration de session.
    /// </summary>
    private void OnSessionExpired(object? sender, EventArgs e)
    {
        // Notifier l'UI que la session a expiré (thread-safe via Dispatcher)
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LogoutRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>
    /// Charge le compteur de notifications non lues.
    /// </summary>
    private async Task LoadUnreadNotificationsCountAsync()
    {
        if (_generateLowStockNotificationsUseCase != null)
        {
            try
            {
                // Génération des notifications de stock bas + comptage des non lues, désormais portés par le use case.
                var result = await _generateLowStockNotificationsUseCase.ExecuteAsync();
                UnreadNotificationsCount = result.UnreadCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur lors du chargement des notifications : {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Rafraîchit le compteur de notifications.
    /// </summary>
    public async Task RefreshNotificationsCountAsync()
    {
        await LoadUnreadNotificationsCountAsync();
    }
}

/// <summary>
/// Représente un élément du menu de navigation.
/// </summary>
public class NavigationItem
{
    public string Icon { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public bool HasBadge { get; set; }
}
