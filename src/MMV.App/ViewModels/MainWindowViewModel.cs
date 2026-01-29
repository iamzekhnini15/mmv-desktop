using System.Collections.ObjectModel;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre principale.
/// Gère la navigation et l'état global de l'application.
/// </summary>
public class MainWindowViewModel : BaseViewModel
{
    private BaseViewModel? _currentView;
    private readonly INavigationService _navigationService;

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
    /// Commande pour naviguer vers une vue.
    /// </summary>
    public ICommand NavigateCommand { get; }

    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        Title = "ManageMyVision";
        
        NavigateCommand = new RelayCommand<string>(ExecuteNavigate, CanNavigate);
        
        InitializeNavigation();
        RegisterViewModels();
        LoadDashboard();
    }

    /// <summary>
    /// Initialise les éléments du menu de navigation.
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
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "💰", 
            Label = "Ventes", 
            ViewName = "Sales" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "📈", 
            Label = "Rapports", 
            ViewName = "Reports" 
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "⚙️", 
            Label = "Paramètres", 
            ViewName = "Settings" 
        });
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
        _navigationService.RegisterViewModel("Reports", typeof(ReportsViewModel));
        _navigationService.RegisterViewModel("Settings", typeof(SettingsViewModel));
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
        
        _navigationService.Navigate(viewName);
        CurrentView = _navigationService.CurrentViewModel;
    }

    /// <summary>
    /// Vérifie si la navigation est possible.
    /// </summary>
    private bool CanNavigate(string? viewName)
    {
        return !string.IsNullOrEmpty(viewName);
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
}
