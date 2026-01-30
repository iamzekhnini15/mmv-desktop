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
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;

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
            // Créer la fenêtre de connexion
            var loginViewModel = new LoginViewModel();
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
                // Utiliser le service provider pour créer MainWindow et MainWindowViewModel avec DI
                var navigationService = _serviceProvider!.GetRequiredService<INavigationService>();
                var mainWindow = new MainWindow(navigationService);
                
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                loginWindow.Close();
            };

            desktop.MainWindow = loginWindow;
        }

        base.OnFrameworkInitializationCompleted();
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
        services.AddScoped<IPrescriptionRepository, PrescriptionRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();

        // Enregistrer les services
        services.AddSingleton<INavigationService, NavigationService>();

        // Enregistrer les ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<PrescriptionsViewModel>();
        services.AddTransient<OrdersViewModel>();
        services.AddTransient<SalesViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();

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
