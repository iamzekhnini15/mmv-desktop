using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMV.Application;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.App.Views;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Interfaces.Time;
using MMV.Domain.Services;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.App;

/// <summary>
/// Classe principale de l'application Avalonia.
/// </summary>
// Base qualifiée explicitement : le namespace MMV.Application (membre du namespace parent MMV)
// masque par résolution de nom simple le type Avalonia.Application (P2B-2B, changement neutre).
public partial class App : Avalonia.Application
{
    private IServiceProvider? _serviceProvider;

    /// <summary>P4-6C : écran de blocage d'un poste PostgreSQL non servi ; <c>null</c> ⇒ démarrage normal.</summary>
    private ServerStartupBlock? _startupBlock;

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
            if (_startupBlock is not null)
            {
                ShowBlockingWindow(desktop, _startupBlock);
            }
            else
            {
                ShowLoginWindow(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// P4-6C (DP-4) : écran de blocage — constat, action attendue, Quitter. Aucune fenêtre de connexion, aucune
    /// session métier, aucune nouvelle tentative (D-15).
    /// </summary>
    private static void ShowBlockingWindow(IClassicDesktopStyleApplicationLifetime desktop, ServerStartupBlock block)
    {
        desktop.MainWindow = new Window
        {
            Title = $"{block.Title} - ManageMyVision",
            Width = 760,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new DatabaseBlockedView
            {
                DataContext = new DatabaseBlockedViewModel(block.Title, block.Message, block.Action, () => desktop.Shutdown())
            }
        };
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
            Width = 1440,
            Height = 900,
            MinWidth = 1280,
            MinHeight = 720,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new LoginView { DataContext = loginViewModel }
        };

        // Quand la connexion réussit, afficher la fenêtre principale
        loginViewModel.LoginSuccessful += (s, e) =>
        {
            var navigationService = _serviceProvider!.GetRequiredService<INavigationService>();
            var dialogService = _serviceProvider!.GetRequiredService<IDialogService>();
            var permissionService = _serviceProvider!.GetRequiredService<IPermissionService>();
            // P2C-GLOBAL : la fenêtre principale ne reçoit plus de repositories directs ; la génération/comptage des
            // notifications de stock bas est portée par un use case Application (composition root).
            var generateLowStockNotificationsUseCase =
                _serviceProvider!.GetRequiredService<IGenerateLowStockNotificationsUseCase>();

            var mainWindow = new MainWindow(navigationService, sessionService, permissionService,
                generateLowStockNotificationsUseCase);

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

        // Fournisseur de base sélectionné par configuration (P4-3, ADR-PROD-DB-002) : variable
        // d'environnement MMV_DATABASE_PROVIDER, défaut SQLite (comportement historique inchangé).
        // Résolu UNE SEULE FOIS pour toute la composition. Un nom de fournisseur invalide bloque ici.
        // P4-8 (D-13, D-14) : la configuration protégée du poste (DPAPI) est la source de production ;
        // toute connexion PostgreSQL est en TLS VerifyFull et SCRAM-SHA-256, sans repli.
        var databaseProviderOptions = DatabaseProviderResolver.Resolve();
        var usesSqlite = databaseProviderOptions.Provider == DatabaseProvider.Sqlite;

        // Chemin de base unique (P2A-1A) : variable d'environnement MMV_DATABASE_PATH,
        // sinon %LOCALAPPDATA%\ManageMyVision\mmv.db. Source unique partagée avec le design-time.
        // Pertinent pour SQLite uniquement : non résolu lorsqu'un fournisseur serveur est sélectionné.
        var databasePath = usesSqlite ? SqliteDatabasePathResolver.ResolveDatabasePath() : null;

        // Enregistrer le DbContext
        services.AddDbContext<OpticDbContext>(options =>
            DatabaseProviderResolver.Configure(options, databaseProviderOptions, databasePath));

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

        // Décrément de stock atomique conditionnel (P2A-1D, R-09) : partage le DbContext de la portée,
        // s'exécute donc dans la transaction ouverte par le runner.
        services.AddScoped<IStockMutationService, EfStockMutationService>();

        // Numérotation fiable des documents (P2A-1E, R-03) : incrément atomique conditionnel d'un compteur
        // persistant ; partage le DbContext de la portée → participe à la transaction de la vente.
        services.AddScoped<INumberSequenceService, EfNumberSequenceService>();

        // Horloge injectable (P4-5D, ADR-PROD-DB-004 T1). SINGLETON, et non Scoped : SystemClock est sans
        // état, ne touche ni au DbContext ni à une transaction, et il ne doit exister qu'UNE horloge dans le
        // processus. L'instance enregistrée est SystemClock.Instance elle-même, celle que lisent les rares
        // chemins non injectés (code-behind Avalonia, défauts de constructeur de ViewModel) — il n'y a donc
        // jamais deux sources de temps divergentes.
        services.AddSingleton<IClock>(SystemClock.Instance);

        // Couche Application (P2B-2B) : squelette créé à vide. AddApplication n'enregistre aucun use case
        // métier réel pour l'instant (aucun changement de comportement). Les use cases (premier cible :
        // EnregistrerVente, P2B-2C) y seront ajoutés en portée Scoped, partageant la transaction.
        services.AddApplication();

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

        // Validation de configuration AVANT toute préparation de base (P2A-1F) : une configuration de
        // seeding explicitement invalide (ex. mot de passe bootstrap trop faible) lève ici et bloque le
        // démarrage. Une configuration absente/ambiguë retombe sur le défaut sûr (Production, sans seed).
        var seedOptions = SeedOptionsResolver.Resolve();

        // Poste PostgreSQL (P4-6C ; ADR-PROD-DB-009 DP-1, DP-4 ; ADR-PROD-DB-010 D-12.3, D-15) — remplace le
        // garde-fou P4-3. AVANT toute opération et hors de tout try (K-13) : disponibilité (une tentative bornée,
        // identité non privilégiée), puis garde de compatibilité, en lecture seule. Non servi ⇒ écran de blocage.
        // Servi ⇒ connexion directe : le poste ne migre jamais (DP-1), ne sème rien (D-12.3 : premier
        // administrateur par MMV.DatabaseManager bootstrap-admin) et n'exécute JAMAIS le cycle de vie SQLite
        // (sauvegarde de fichier, migrations, adoption) contre un serveur.
        if (!usesSqlite)
        {
            _startupBlock = ServerStartupCheck.Run(databaseProviderOptions.ConnectionString!, () =>
            {
                var options = new DbContextOptionsBuilder<OpticDbContext>();
                DatabaseProviderResolver.Configure(options, databaseProviderOptions, null);
                return new OpticDbContext(options.Options);
            });
            return serviceProvider;
        }

        // À partir d'ici, SQLite est le fournisseur retenu : le chemin de fichier est donc résolu.
        var sqliteDatabasePath = databasePath!;

        // Préparer la base : cycle de vie SQLite professionnel et sûr (P2A-1A).
        try
        {
            var journalPath = Path.Combine(
                Path.GetDirectoryName(sqliteDatabasePath) ?? ".", "migration-journal.log");
            var databaseManager = new SqliteDatabaseManager(new MigrationJournal(journalPath));

            System.Diagnostics.Debug.WriteLine($"[App] Database path: {sqliteDatabasePath}");

            // 0) Reprise contrôlée de l'ancien fichier mmv-optic.db (P2A-1B) — sûre et sans perte :
            //    diagnostiquée, copiée vers le chemin courant UNIQUEMENT si valide/compatible et que
            //    le chemin courant est vide ; jamais de suppression/écrasement de l'ancien fichier ;
            //    conflit (les deux présents) = ancien conservé, courant utilisé. Toujours journalisée.
            //    P4-3 : cette reprise concerne EXCLUSIVEMENT un ancien FICHIER SQLite local ; son DbContext
            //    ad hoc reste donc délibérément SQLite-only et n'est jamais multi-fournisseur. Elle n'est
            //    atteinte que lorsque SQLite est le fournisseur sélectionné (cf. garde-fou ci-dessus).
            var legacyPath = SqliteDatabasePathResolver.ResolveLegacyDatabasePath();
            var recovery = new LegacyDatabaseRecoveryService(databaseManager.Journal);
            var recoveryResult = recovery.Recover(
                legacyPath,
                sqliteDatabasePath,
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

                // 2) Seeding gouverné par l'environnement (P2A-1F, R-16/R-17) : en production, aucune donnée
                //    de démonstration et aucun compte admin/admin actif (sécurisé via secret bootstrap ou
                //    neutralisé) ; jeu de démonstration uniquement si explicitement activé en
                //    Development/Demonstration. Aucun secret n'est journalisé (logs de démarrage non sensibles).
                var seedResult = new DatabaseSeeder().Seed(dbContext, seedOptions);
                System.Diagnostics.Debug.WriteLine(
                    $"[App] Seed: env={seedResult.Environment}, demo={seedResult.DemoSeedApplied}, " +
                    $"bootstrapAdmin={seedResult.BootstrapAdminConfigured}, " +
                    $"weakAdminNeutralized={seedResult.WeakDefaultAdminNeutralized}");
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
