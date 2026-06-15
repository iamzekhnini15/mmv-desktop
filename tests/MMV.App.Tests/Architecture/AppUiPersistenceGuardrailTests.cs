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
        "CustomerDetailViewModel -> ICustomerRepository",
        "CustomerDetailViewModel -> IPrescriptionRepository",
        "CustomerDetailViewModel -> IProductRepository",
        "CustomerDetailViewModel -> ISaleRepository",
        // P2C-2 : "CustomerFormViewModel -> ICustomerRepository" retiré — la VM délègue désormais à
        // ICreateCustomerUseCase / IUpdateCustomerUseCase (couche Application).
        "CustomerInfoViewModel -> ISaleRepository",
        // CustomerPrescriptionsViewModel conserve IPrescriptionRepository pour ses lectures d'affichage
        // (LoadPrescriptionsAsync). Les écritures (create/update/delete) sont passées aux use cases en P2C-4.
        "CustomerPrescriptionsViewModel -> IPrescriptionRepository",
        "CustomerPurchaseHistoryViewModel -> ISaleRepository",
        "CustomersListViewModel -> ICustomerRepository",
        "CustomersViewModel -> ICustomerRepository",
        "CustomersViewModel -> IPrescriptionRepository",
        "CustomersViewModel -> IProductRepository",
        "CustomersViewModel -> ISaleRepository",
        "InventoryViewModel -> IProductRepository",
        "InventoryViewModel -> IStockMovementRepository",
        "MainWindowViewModel -> INotificationRepository",
        "MainWindowViewModel -> IProductRepository",
        "NotificationsListViewModel -> INotificationRepository",
        "NotificationsListViewModel -> IProductRepository",
        "NotificationsViewModel -> INotificationRepository",
        "NotificationsViewModel -> IProductRepository",
        "OrderFormViewModel -> ICustomerRepository",
        "OrderFormViewModel -> IPrescriptionRepository",
        "OrderFormViewModel -> IProductRepository",
        "OrderKanbanViewModel -> IOrderRepository",
        "OrdersListViewModel -> IOrderRepository",
        "OrdersViewModel -> ICustomerRepository",
        "OrdersViewModel -> IOrderRepository",
        "OrdersViewModel -> IPrescriptionRepository",
        "OrdersViewModel -> IProductRepository",
        // P2C-4 : "PrescriptionDetailViewModel -> IPrescriptionRepository" retiré — la VM de détail est purement
        // présentationnelle (la dépendance injectée était morte) et ne reçoit plus aucun port de persistance.
        // P2C-4 : "PrescriptionFormViewModel -> IPrescriptionRepository" retiré — la création/modification d'ordonnance
        // est désormais portée par ICreatePrescriptionUseCase / IUpdatePrescriptionUseCase (couche Application).
        "ProductFormViewModel -> INotificationRepository",
        "ProductFormViewModel -> IProductRepository",
        "ProductFormViewModel -> ISupplierRepository",
        "ProductsListViewModel -> IProductRepository",
        "ProductsViewModel -> IProductRepository",
        "ProductsViewModel -> ISupplierRepository",
        "SaleFormViewModel -> IPrescriptionRepository",
        "SaleFormViewModel -> IProductRepository",
        "StockMovementFormViewModel -> IProductRepository",
        "StockMovementsListViewModel -> IProductRepository",
        "StockMovementsListViewModel -> IStockMovementRepository",
        "StockMovementsViewModel -> IProductRepository",
        "StockMovementsViewModel -> IStockMovementRepository",
        "SupplierFormViewModel -> ISupplierRepository",
        "SuppliersListViewModel -> ISupplierRepository",
        "SuppliersViewModel -> ISupplierRepository",
        "UserFormViewModel -> IUserRepository",
        "UsersListViewModel -> IUserRepository",
        "UsersViewModel -> IUserRepository",
    };

    /// <summary>
    /// VM ⇒ <c>IUnitOfWork</c> par paramètre de constructeur. Format : <c>"NomViewModel -> IUnitOfWork"</c>.
    /// TODO P2C : réduire à chaque phase (la persistance/transaction appartient à la couche Application).
    /// </summary>
    private static readonly HashSet<string> AllowedViewModelUnitOfWorkConstructorDependencies = new()
    {
        // CustomerDetailViewModel conserve IUnitOfWork (param de constructeur) : fiche client détaillée, hors
        // périmètre P2C-4. La dépendance n'est plus utilisée par les enfants ordonnance (passés aux use cases) ;
        // son extraction relève d'une phase « fiche client » ultérieure.
        "CustomerDetailViewModel -> IUnitOfWork",
        // P2C-2 : "CustomerFormViewModel -> IUnitOfWork" retiré — la persistance/transaction est portée par les
        // use cases client (couche Application), plus par la VM.
        // P2C-4 : "CustomerPrescriptionsViewModel -> IUnitOfWork" retiré — la suppression d'ordonnance est désormais
        // portée par IDeletePrescriptionUseCase (couche Application). La VM ne conserve que IPrescriptionRepository
        // pour ses lectures d'affichage (LoadPrescriptionsAsync).
        // P2C-3 : "CustomersListViewModel -> IUnitOfWork" retiré — la suppression client est désormais portée par
        // IDeleteCustomerUseCase (couche Application). La VM ne conserve que ICustomerRepository pour ses lectures
        // d'affichage (LoadCustomersAsync), dette reportée vers des query use cases (roadmap §6).
        "CustomersViewModel -> IUnitOfWork",
        "InventoryViewModel -> IUnitOfWork",
        "MainWindowViewModel -> IUnitOfWork",
        "NotificationsListViewModel -> IUnitOfWork",
        "NotificationsViewModel -> IUnitOfWork",
        "OrderKanbanViewModel -> IUnitOfWork",
        "OrdersViewModel -> IUnitOfWork",
        // P2C-4 : "PrescriptionFormViewModel -> IUnitOfWork" retiré — la création/modification d'ordonnance est portée
        // par ICreatePrescriptionUseCase / IUpdatePrescriptionUseCase (couche Application).
        "ProductFormViewModel -> IUnitOfWork",
        "ProductsListViewModel -> IUnitOfWork",
        "ProductsViewModel -> IUnitOfWork",
        "SupplierFormViewModel -> IUnitOfWork",
        "SuppliersListViewModel -> IUnitOfWork",
        "SuppliersViewModel -> IUnitOfWork",
        "UserFormViewModel -> IUnitOfWork",
        "UsersListViewModel -> IUnitOfWork",
        "UsersViewModel -> IUnitOfWork",
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
        "src/MMV.App/App.axaml.cs",
        // P2C-2 : CustomerFormView.axaml.cs et CustomersView.axaml.cs retirés — la persistance directe
        // (Repository/UnitOfWork/SaveChangesAsync) du flux client create/update a été supprimée du code-behind
        // au profit des use cases Application (via CustomerFormViewModel).
        "src/MMV.App/Views/MainWindow.axaml.cs",
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
