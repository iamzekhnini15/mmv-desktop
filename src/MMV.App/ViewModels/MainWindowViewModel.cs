using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel de la fenêtre principale.
/// Gère la navigation et l'état global de l'application.
/// </summary>
public class MainWindowViewModel : BaseViewModel
{
    private BaseViewModel? _currentView;
    private readonly INavigationService _navigationService;
    private readonly INotificationRepository? _notificationRepository;
    private readonly IProductRepository? _productRepository;
    private readonly IUnitOfWork? _unitOfWork;
    private int _unreadNotificationsCount;

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
    /// Commande pour naviguer vers une vue.
    /// </summary>
    public ICommand NavigateCommand { get; }

    public MainWindowViewModel(
        INavigationService navigationService,
        INotificationRepository? notificationRepository = null,
        IProductRepository? productRepository = null,
        IUnitOfWork? unitOfWork = null)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _notificationRepository = notificationRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        Title = "ManageMyVision";
        
        NavigateCommand = new RelayCommand<string>(ExecuteNavigate, CanNavigate);
        
        InitializeNavigation();
        RegisterViewModels();
        LoadDashboard();
        
        // Charger le compteur de notifications
        _ = LoadUnreadNotificationsCountAsync();
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
            Icon = "�", 
            Label = "Notifications", 
            ViewName = "Notifications",
            HasBadge = true
        });
        NavigationItems.Add(new NavigationItem 
        { 
            Icon = "�📈", 
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
        _navigationService.RegisterViewModel("Notifications", typeof(NotificationsViewModel));
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

    /// <summary>
    /// Charge le compteur de notifications non lues.
    /// </summary>
    private async Task LoadUnreadNotificationsCountAsync()
    {
        if (_notificationRepository != null && _productRepository != null && _unitOfWork != null)
        {
            try
            {
                // Générer les notifications de stock bas
                var products = await _productRepository.GetAllAsync();
                var lowStockProducts = products.Where(p => p.StockQuantity <= p.StockAlertThreshold).ToList();

                foreach (var product in lowStockProducts)
                {
                    var notifications = await _notificationRepository.GetAllAsync();
                    var exists = notifications.Any(n => 
                        n.Type == "LowStock" && 
                        n.EntityId == product.ProductId && 
                        !n.IsRead);

                    if (!exists)
                    {
                        var notification = new Domain.Entities.Notification
                        {
                            Type = "LowStock",
                            Title = $"Stock bas : {product.Name}",
                            Message = $"Le produit {product.Reference} - {product.Name} est en stock bas ({product.StockQuantity}/{product.StockAlertThreshold})",
                            EntityId = product.ProductId,
                            EntityType = "Product",
                            IsRead = false,
                            CreatedAt = DateTime.Now
                        };

                        await _notificationRepository.CreateAsync(notification);
                    }
                }

                await _unitOfWork.SaveChangesAsync();

                // Charger le compteur
                UnreadNotificationsCount = await _notificationRepository.CountUnreadAsync();
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
