using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.App.Views;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
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

        // Chemin de base unique (P2A-1A) : variable d'environnement MMV_DATABASE_PATH,
        // sinon %LOCALAPPDATA%\ManageMyVision\mmv.db. Source unique partagée avec le design-time.
        var databasePath = SqliteDatabasePathResolver.ResolveDatabasePath();

        // Enregistrer le DbContext
        services.AddDbContext<OpticDbContext>(options =>
            options.UseSqlite(SqliteDatabasePathResolver.GetConnectionString(databasePath)));

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

        // Frontière transactionnelle réutilisable (P2A-1C, R-23) : partage le DbContext de la portée.
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();

        // Enregistrer les services d'authentification et de session
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IThemeService, ThemeService>();

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

        // Préparer la base : cycle de vie SQLite professionnel et sûr (P2A-1A).
        try
        {
            var journalPath = Path.Combine(
                Path.GetDirectoryName(databasePath) ?? ".", "migration-journal.log");
            var databaseManager = new SqliteDatabaseManager(new MigrationJournal(journalPath));

            System.Diagnostics.Debug.WriteLine($"[App] Database path: {databasePath}");

            // 0) Reprise contrôlée de l'ancien fichier mmv-optic.db (P2A-1B) — sûre et sans perte :
            //    diagnostiquée, copiée vers le chemin courant UNIQUEMENT si valide/compatible et que
            //    le chemin courant est vide ; jamais de suppression/écrasement de l'ancien fichier ;
            //    conflit (les deux présents) = ancien conservé, courant utilisé. Toujours journalisée.
            var legacyPath = SqliteDatabasePathResolver.ResolveLegacyDatabasePath();
            var recovery = new LegacyDatabaseRecoveryService(databaseManager.Journal);
            var recoveryResult = recovery.Recover(
                legacyPath,
                databasePath,
                path => new OpticDbContext(new DbContextOptionsBuilder<OpticDbContext>()
                    .UseSqlite(SqliteDatabasePathResolver.GetConnectionString(path)).Options));
            System.Diagnostics.Debug.WriteLine(
                $"[App] Legacy recovery: decision={recoveryResult.Decision}, copied={recoveryResult.CopyPerformed}, " +
                $"conflict={recoveryResult.ConflictDetected}");

            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<OpticDbContext>();

                // 1) Schéma : sauvegarde + détection + migrations/adoption + journal (P2A-1A).
                var result = databaseManager.PrepareDatabase(dbContext);
                System.Diagnostics.Debug.WriteLine(
                    $"[App] Database prepared: state={result.DetectedState}, fresh={result.WasFreshInstall}, " +
                    $"adopted={result.WasAdopted}, backup={result.BackupPath ?? "none"}");

                // 2) Seed admin + jeu de démonstration existant — comportement INCHANGÉ
                //    (suppression du seed démo = R-16/Étape 1F, hors périmètre P2A-1A).
                //    EnsureCreated() y est désormais un no-op : la base existe déjà après migration.
                DbInitializer.Initialize(dbContext);
                System.Diagnostics.Debug.WriteLine($"[App] Database initialized with {dbContext.Customers.Count()} customers");
            }
        }
        catch (DatabaseMigrationException dbEx)
        {
            // Échec explicite et compréhensible : aucune donnée supprimée (base + sauvegarde conservées).
            System.Diagnostics.Debug.WriteLine($"[App] DATABASE MIGRATION FAILURE: {dbEx.Message}");
            System.Diagnostics.Debug.WriteLine($"[App] Cause: {dbEx.InnerException?.Message}");
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] ERROR initializing database: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[App] Stack trace: {ex.StackTrace}");
        }

        return serviceProvider;
    }
}
