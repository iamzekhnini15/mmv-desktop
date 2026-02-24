using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.App.Views;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.App;

/// <summary>
/// Classe principale de l'application Avalonia.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Configurer l'injection de dépendances
        _serviceProvider = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShowLoginWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Affiche la fenêtre de connexion.
    /// </summary>
    private void ShowLoginWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var authService = _serviceProvider!.GetRequiredService<IAuthenticationService>();
        var sessionService = _serviceProvider!.GetRequiredService<ISessionService>();

        var loginViewModel = new LoginViewModel(authService, sessionService);
        var loginWindow = new Window
        {
            Title = "Connexion - ManageMyVision",
            Width = 1000,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new LoginView { DataContext = loginViewModel }
        };

        // Quand la connexion réussit, afficher la fenêtre principale
        loginViewModel.LoginSuccessful += (s, e) =>
        {
            var navigationService = _serviceProvider!.GetRequiredService<INavigationService>();
            var dialogService = _serviceProvider!.GetRequiredService<IDialogService>();
            var permissionService = _serviceProvider!.GetRequiredService<IPermissionService>();
            var notificationRepository = _serviceProvider!.GetRequiredService<INotificationRepository>();
            var productRepository = _serviceProvider!.GetRequiredService<IProductRepository>();
            var unitOfWork = _serviceProvider!.GetRequiredService<IUnitOfWork>();

            var mainWindow = new MainWindow(navigationService, sessionService, permissionService,
                notificationRepository, productRepository, unitOfWork);

            // Configurer DialogService avec la MainWindow
            if (dialogService is DialogService ds)
            {
                ds.SetMainWindow(mainWindow);
            }

            // Gérer la déconnexion
            if (mainWindow.DataContext is MainWindowViewModel mainWindowVm)
            {
                mainWindowVm.LogoutRequested += (sender, args) =>
                {
                    sessionService.Logout();
                    mainWindow.Close();
                    ShowLoginWindow(desktop);
                };
            }

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            loginWindow.Close();
        };

        desktop.MainWindow = loginWindow;
    }

    /// <summary>
    /// Configure le conteneur de dépendances.
    /// </summary>
    private IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Enregistrer le DbContext
        services.AddDbContext<OpticDbContext>(options =>
            options.UseSqlite("Data Source=mmv-optic.db"));

        // Enregistrer UnitOfWork et Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductCategoryRepository, ProductCategoryRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IPrescriptionRepository, PrescriptionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IStockMovementRepository, StockMovementRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Enregistrer les services d'authentification et de session
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IPermissionService, PermissionService>();

        // Enregistrer les services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();

        // Enregistrer les ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<PrescriptionsViewModel>();
        services.AddTransient<OrdersViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<NotificationsViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<UserProfileViewModel>();

        var serviceProvider = services.BuildServiceProvider();
        
        // Initialiser la base de données avec des données test
        try
        {
            System.Diagnostics.Debug.WriteLine("[App] Initializing database...");
            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<OpticDbContext>();
                System.Diagnostics.Debug.WriteLine($"[App] Database path: {dbContext.Database.GetConnectionString()}");
                DbInitializer.Initialize(dbContext);
                System.Diagnostics.Debug.WriteLine($"[App] Database initialized with {dbContext.Customers.Count()} customers");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] ERROR initializing database: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");
        }

        return serviceProvider;
    }
}
