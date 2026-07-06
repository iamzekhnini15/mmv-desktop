using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MMV.App.ViewModels;
using MMV.Domain.Interfaces.Repositories;
using Xunit;

namespace MMV.App.Tests.Architecture;

/// <summary>
/// P2C-1 — Garde-fous d'architecture UI ⇄ persistance (cf. plan de migration P2C : « UI = affichage +
/// état écran + envoi de données ; Application = orchestration métier + persistance »).
/// <para>
/// Objectif P2C : faire disparaître progressivement les dépendances directes de la couche UI
/// (<c>MMV.App</c>) vers les <c>*Repository</c> du Domain et vers <c>IUnitOfWork</c>, au profit
/// d'<i>use cases</i> de la couche Application.
/// </para>
/// <para>
/// Comme il subsiste aujourd'hui de nombreuses dépendances, ces tests ne peuvent pas encore imposer
/// « zéro violation ». Ils fonctionnent donc en <b>baseline contrôlée</b> :
/// <list type="bullet">
///   <item>les violations existantes sont listées explicitement dans des <i>allowlists</i> ;</item>
///   <item>toute violation détectée mais absente de l'allowlist fait échouer le test ;</item>
///   <item>à chaque phase P2C suivante, les allowlists doivent <b>diminuer</b>, jamais grossir.</item>
/// </list>
/// </para>
/// <para>
/// Les chemins sont normalisés avec <c>/</c> pour rester stables sous Windows comme sous Linux (CI).
/// </para>
/// </summary>
public sealed class AppUiPersistenceGuardrailTests
{
    private static readonly Assembly AppAssembly = typeof(BaseViewModel).Assembly;

    private const string DomainRepositoriesNamespace = "MMV.Domain.Interfaces.Repositories";

    // -----------------------------------------------------------------------------------------------
    // ALLOWLISTS — baseline P2C-1.
    // RÈGLE : ces collections ne doivent que DIMINUER au fil des phases P2C. N'ajoutez une entrée que
    // si une dépendance existante a été oubliée ici (jamais pour autoriser une nouvelle dépendance UI).
    // -----------------------------------------------------------------------------------------------

    /// <summary>
    /// VM ⇒ <c>*Repository</c> par paramètre de constructeur. Format : <c>"NomViewModel -> IXxxRepository"</c>.
    /// TODO P2C : réduire à chaque phase (migrer la dépendance vers un use case Application).
    /// </summary>
    private static readonly HashSet<string> AllowedViewModelRepositoryConstructorDependencies = new()
    {
        // P2C-GLOBAL : après extraction de TOUTES les écritures restantes vers la couche Application, les entrées
        // ci-dessous ne correspondent plus qu'à des LECTURES d'affichage (chargement de listes / détail). Elles
        // constituent la dette ouverte adressée par P2D (query use cases + DTO applicatifs).
        //
        // P2D (LECTURES → query use cases). Les entrées suivantes ont été RETIRÉES au fil des étapes P2D, la lecture
        // étant désormais portée par un query use case Application renvoyant des DTO plats (jamais d'entité EF) :
        //   - P2D-1 Utilisateurs : "UsersListViewModel -> IUserRepository", "UsersViewModel -> IUserRepository"
        //     (IListUsersUseCase) ;
        //   - P2D-2 Fournisseurs : "SuppliersListViewModel -> ISupplierRepository",
        //     "SuppliersViewModel -> ISupplierRepository" (IListSuppliersUseCase + IGetSupplierWithProductsUseCase) ;
        //   - P2D-3 Notifications : "NotificationsListViewModel -> INotificationRepository",
        //     "NotificationsViewModel -> INotificationRepository" (IListNotificationsUseCase +
        //     ICountUnreadNotificationsUseCase).
        //
        // RELIQUAT P2D (documenté dans docs/implementation/P2D-4-report.md et docs/implementation/P2D-GLOBAL-report.md
        // §17) : les lectures des modules Clients et Commandes/Ventes restent portées par des repositories injectés
        // (écrans composites à graphes d'entités : fiches détaillées, données de référence de formulaires, Kanban).
        // Elles seront migrées en P2D-5/6. Cette allowlist ne doit toujours que DIMINUER.
        //
        // P2D-4 (Produits / Stock) : les lectures des sélecteurs produit (StockMovementsListViewModel,
        // StockMovementFormViewModel), de l'inventaire (InventoryViewModel), des mouvements de stock
        // (StockMovementsViewModel) et du sélecteur fournisseur du formulaire produit (ProductFormViewModel) ont été
        // migrées vers des query use cases Application (IListStockMovementsUseCase, IListProductsForPickerUseCase,
        // IGetInventoryOverviewUseCase, IListSuppliersUseCase réutilisé). Les 9 entrées correspondantes ont été
        // retirées. Reliquat P2D-4 justifié : ProductsListViewModel / ProductsViewModel conservent IProductRepository
        // pour la liste produit à graphe d'entités (Product) qui alimente la fiche détaillée (ProductDetailViewModel,
        // historique de commandes) et le formulaire d'édition (détails Verre/Lentille/Accessoire) via de nombreux
        // événements typés entité — migration non réalisable en iso-fonctionnel sans exécution UI de recette.
        //
        // P2D-5 (Clients / Ordonnances) — GO PARTIEL (cf. docs/implementation/P2D-5-report.md). Migrées vers des query
        // use cases Application (IGetCustomerPurchaseHistoryUseCase, IListPrescriptionsByCustomerUseCase) : l'historique
        // d'achats (CustomerPurchaseHistoryViewModel, onglet Infos de CustomerInfoViewModel) et la liste des ordonnances
        // du client (CustomerPrescriptionsViewModel). 6 entrées retirées : "CustomerDetailViewModel -> ICustomerRepository"
        // (dépendance morte), "CustomerDetailViewModel -> ISaleRepository", "CustomerInfoViewModel -> ISaleRepository",
        // "CustomerPrescriptionsViewModel -> IPrescriptionRepository", "CustomerPurchaseHistoryViewModel -> ISaleRepository",
        // "CustomersViewModel -> ISaleRepository". Reliquat P2D-5 justifié (verrouillé par cette allowlist qui ne fait que
        // diminuer) : CustomersListViewModel / CustomersViewModel conservent ICustomerRepository pour la liste clients
        // (entité Customer threadée vers le formulaire d'ÉDITION CustomerFormViewModel et la fiche détail — migration non
        // réalisable en iso-fonctionnel sans exécution UI de recette) ; CustomerDetailViewModel / CustomersViewModel
        // conservent IProductRepository + IPrescriptionRepository uniquement pour construire SaleFormViewModel (lectures de
        // référence du formulaire de vente, à migrer en P2D-6 / Ventes).
        "CustomerDetailViewModel -> IPrescriptionRepository",
        "CustomerDetailViewModel -> IProductRepository",
        "CustomersListViewModel -> ICustomerRepository",
        "CustomersViewModel -> ICustomerRepository",
        "CustomersViewModel -> IPrescriptionRepository",
        "CustomersViewModel -> IProductRepository",
        "OrderFormViewModel -> ICustomerRepository",
        "OrderFormViewModel -> IPrescriptionRepository",
        "OrderFormViewModel -> IProductRepository",
        "OrderKanbanViewModel -> IOrderRepository",
        "OrdersListViewModel -> IOrderRepository",
        "OrdersViewModel -> ICustomerRepository",
        "OrdersViewModel -> IOrderRepository",
        "OrdersViewModel -> IPrescriptionRepository",
        "OrdersViewModel -> IProductRepository",
        "ProductsListViewModel -> IProductRepository",
        "ProductsViewModel -> IProductRepository",
        "SaleFormViewModel -> IPrescriptionRepository",
        "SaleFormViewModel -> IProductRepository",
    };

    /// <summary>
    /// VM ⇒ <c>IUnitOfWork</c> par paramètre de constructeur. Format : <c>"NomViewModel -> IUnitOfWork"</c>.
    /// TODO P2C : réduire à chaque phase (la persistance/transaction appartient à la couche Application).
    /// </summary>
    private static readonly HashSet<string> AllowedViewModelUnitOfWorkConstructorDependencies = new()
    {
        // P2C-GLOBAL : allowlist VIDÉE. Plus AUCUN ViewModel ne dépend d'IUnitOfWork. Toutes les écritures
        // (client, ordonnance, produit, fournisseur, utilisateur, notification, stock, avancement de commande) sont
        // désormais portées par des use cases de la couche Application, qui possèdent seuls la frontière
        // transactionnelle (SaveChangesAsync). Les précédentes entrées (CustomerDetailViewModel, CustomersViewModel,
        // InventoryViewModel, MainWindowViewModel, Notifications*, OrderKanbanViewModel, OrdersViewModel, Product*,
        // Supplier*, User*) ont toutes été retirées.
    };

    /// <summary>
    /// Propriétés publiques de VM exposant <c>IUnitOfWork</c> ou un <c>*Repository</c>.
    /// Format : <c>"NomViewModel.NomPropriété"</c>.
    /// TODO P2C : réduire à zéro (une VM ne doit pas ré-exposer ses ports de persistance).
    /// </summary>
    private static readonly HashSet<string> AllowedPublicPersistenceProperties = new()
    {
        // P2C-3 : "CustomersListViewModel.Repository" et "CustomersListViewModel.UnitOfWork" retirés — la fuite de
        // ports de persistance vers le code-behind est éliminée (la suppression passe par IDeleteCustomerUseCase,
        // le repository n'est plus exposé publiquement). Allowlist vidée : aucun ViewModel ne ré-expose ses ports.
    };

    /// <summary>
    /// Fichiers de code-behind <c>*.axaml.cs</c> autorisés à mentionner des jetons de persistance.
    /// Chemins relatifs à la racine du dépôt, normalisés avec <c>/</c>.
    /// <c>App.axaml.cs</c> est le <i>composition root</i> (DI) : autorisé temporairement.
    /// TODO P2C : réduire (déplacer la persistance des vues vers les use cases / VM).
    /// </summary>
    private static readonly HashSet<string> AllowedCodeBehindPersistenceFiles = new()
    {
        // App.axaml.cs est le composition root (DI) : seul code-behind autorisé à mentionner des jetons de
        // persistance (il enregistre repositories/UnitOfWork/DbContext dans le conteneur).
        "src/MMV.App/App.axaml.cs",
        // P2C-GLOBAL : "src/MMV.App/Views/MainWindow.axaml.cs" retiré — le code-behind ne référence plus aucun
        // repository ni IUnitOfWork ; la fenêtre principale reçoit désormais IGenerateLowStockNotificationsUseCase
        // (couche Application) au lieu des trois ports de persistance.
    };

    /// <summary>Jetons de persistance recherchés dans les code-behind.</summary>
    private static readonly string[] PersistenceTokens =
    {
        "SaveChangesAsync",
        "CreateAsync",
        "UpdateAsync",
        "DeleteAsync",
        "IUnitOfWork",
        "Repository",
        "DbContext",
    };

    // -----------------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------------

    private static IEnumerable<Type> ViewModelTypes() => AppAssembly
        .GetTypes()
        .Where(t => t.IsClass && t.Name.EndsWith("ViewModel", StringComparison.Ordinal));

    private static bool IsRepositoryType(Type t) =>
        t.Name.EndsWith("Repository", StringComparison.Ordinal)
        || (string.Equals(t.Namespace, DomainRepositoriesNamespace, StringComparison.Ordinal)
            && t != typeof(IUnitOfWork));

    private static bool IsUnitOfWorkType(Type t) => t == typeof(IUnitOfWork);

    private static string FormatAsAllowlist(IEnumerable<string> entries) =>
        string.Join(Environment.NewLine, entries.OrderBy(e => e, StringComparer.Ordinal).Select(e => $"    \"{e}\","));

    private static void AssertBaseline(
        ISet<string> detected,
        ISet<string> allowlist,
        string newViolationHeader,
        string remediation)
    {
        var newViolations = detected.Except(allowlist).OrderBy(e => e, StringComparer.Ordinal).ToList();
        var staleAllowlist = allowlist.Except(detected).OrderBy(e => e, StringComparer.Ordinal).ToList();

        Assert.True(newViolations.Count == 0,
            $"{newViolationHeader}{Environment.NewLine}" +
            $"{FormatAsAllowlist(newViolations)}{Environment.NewLine}" +
            $"{remediation}");

        Assert.True(staleAllowlist.Count == 0,
            "The allowlist must shrink, not lie: these entries no longer exist and must be removed " +
            $"from the allowlist:{Environment.NewLine}{FormatAsAllowlist(staleAllowlist)}");
    }

    // -----------------------------------------------------------------------------------------------
    // 1. VM — dépendances constructeur vers les repositories
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void ViewModels_DoNotDependOnRepositories_OutsideAllowlist()
    {
        var detected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vm in ViewModelTypes())
        foreach (var ctor in vm.GetConstructors())
        foreach (var p in ctor.GetParameters())
        {
            if (IsRepositoryType(p.ParameterType))
                detected.Add($"{vm.Name} -> {p.ParameterType.Name}");
        }

        AssertBaseline(
            detected,
            AllowedViewModelRepositoryConstructorDependencies,
            "New UI persistence dependency detected (ViewModel constructor depends on a repository):",
            "Either migrate this dependency to an Application use case, " +
            "or add it explicitly to the allowlist with justification.");
    }

    // -----------------------------------------------------------------------------------------------
    // 2. VM — dépendance constructeur vers IUnitOfWork
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void ViewModels_DoNotDependOnUnitOfWork_OutsideAllowlist()
    {
        var detected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vm in ViewModelTypes())
        foreach (var ctor in vm.GetConstructors())
        {
            if (ctor.GetParameters().Any(p => IsUnitOfWorkType(p.ParameterType)))
                detected.Add($"{vm.Name} -> IUnitOfWork");
        }

        AssertBaseline(
            detected,
            AllowedViewModelUnitOfWorkConstructorDependencies,
            "New UI persistence dependency detected (ViewModel constructor depends on IUnitOfWork):",
            "Either migrate this dependency to an Application use case, " +
            "or add it explicitly to the allowlist with justification.");
    }

    // -----------------------------------------------------------------------------------------------
    // 3. VM — propriétés publiques exposant Repository / IUnitOfWork
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void ViewModels_DoNotExposePersistenceProperties_OutsideAllowlist()
    {
        var detected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var vm in ViewModelTypes())
        foreach (var prop in vm.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (IsUnitOfWorkType(prop.PropertyType) || IsRepositoryType(prop.PropertyType))
                detected.Add($"{vm.Name}.{prop.Name}");
        }

        AssertBaseline(
            detected,
            AllowedPublicPersistenceProperties,
            "New UI persistence dependency detected (public property leaking a persistence port):",
            "Either migrate this dependency to an Application use case, " +
            "or add it explicitly to the allowlist with justification.");
    }

    // -----------------------------------------------------------------------------------------------
    // 4. Code-behind — persistance directe dans les *.axaml.cs
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void CodeBehind_DoesNotPerformPersistence_OutsideAllowlist()
    {
        var repoRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repoRoot, "src", "MMV.App");
        Assert.True(Directory.Exists(appRoot),
            $"Le test doit pouvoir localiser le code source de MMV.App (cherché dans : {appRoot}).");

        var detected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.axaml.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            if (PersistenceTokens.Any(token => content.Contains(token, StringComparison.Ordinal)))
            {
                var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                detected.Add(relative);
            }
        }

        AssertBaseline(
            detected,
            AllowedCodeBehindPersistenceFiles,
            "New UI persistence dependency detected (code-behind performing persistence):",
            "Either move this logic to a ViewModel / Application use case, " +
            "or add the file explicitly to the allowlist with justification.");
    }

    // -----------------------------------------------------------------------------------------------
    // 5. Contrôle positif — MMV.App reste bien la couche qui consomme MMV.Application.
    //    (Ne relâche pas la pureté de MMV.Application, verrouillée côté MMV.Application.Tests.)
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void App_References_Application()
    {
        var referenced = AppAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.Contains("MMV.Application", referenced);
    }

    // -----------------------------------------------------------------------------------------------

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MMV.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null,
            $"Le fichier solution MMV.sln doit être trouvable en remontant depuis {AppContext.BaseDirectory}.");

        return dir!.FullName;
    }
}
