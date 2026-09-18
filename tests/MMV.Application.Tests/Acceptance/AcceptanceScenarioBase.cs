using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Customers.CreateCustomer;
using MMV.Application.UseCases.Customers.SetCustomerArchived;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.SetProductActive;
using MMV.Application.UseCases.Sales.RegisterSale;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Application.UseCases.Suppliers.CreateSupplier;
using MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;
using MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;
using MMV.Domain.Enums;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;

namespace MMV.Application.Tests.Acceptance;

/// <summary>
/// Socle des scénarios de recette P3-11 : vraie base SQLite <b>migrée</b>, une par test, initialisée par le
/// chemin de seed <b>Production sûr</b> réellement emprunté au démarrage desktop.
/// </summary>
/// <remarks>
/// <para>
/// <b>Différences assumées avec les socles de tests ciblés existants.</b> Deux choix diffèrent volontairement de
/// <c>WorkshopSheetTestBase</c> et consorts :
/// </para>
/// <list type="bullet">
///   <item><b>Schéma par <c>Migrate()</c>, jamais <c>EnsureCreated()</c>.</b> C'est ce que fait la production
///   (<c>SqliteDatabaseManager</c>). <c>EnsureCreated()</c> construit le schéma depuis le modèle sans exécuter le
///   SQL personnalisé des migrations : l'index unique filtré <c>idx_notifications_active_low_stock_unique</c>
///   (P3-8), créé par SQL de migration, y serait absent. Une recette doit observer la base telle qu'elle est en
///   production.</item>
///   <item><b>Clés étrangères ACTIVÉES.</b> Les tests P3-6/6B les désactivent parce qu'ils fabriquent des
///   commandes dont le <c>SaleId</c> est orphelin. Le parcours de recette passe exclusivement par
///   <see cref="RegisterSaleUseCase"/>, qui renseigne toujours <c>SaleId</c> : aucune dérogation n'est
///   nécessaire, et l'intégrité référentielle réelle est donc opposée aux scénarios.</item>
/// </list>
/// <para>
/// <b>Ce socle n'est pas un conteneur DI.</b> Il ne construit ni <c>IServiceCollection</c>, ni
/// <c>ServiceProvider</c>, et ne référence jamais <c>MMV.App</c>. <see cref="AcceptanceScope"/> instancie
/// directement les dépôts et primitives autour d'un <see cref="OpticDbContext"/> partagé, ce qui reproduit
/// exactement la portée <c>AddScoped</c> de production sans en dupliquer la composition.
/// </para>
/// </remarks>
public abstract class AcceptanceScenarioBase : IDisposable
{
    private readonly string _workDirectory;

    protected AcceptanceScenarioBase()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-11-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        // Sans libération des pools, le fichier reste verrouillé sous Windows et la suppression échoue.
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch
        {
            // Nettoyage best-effort : un fichier resté verrouillé ne doit jamais faire échouer un scénario.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Prépare une base neuve pour un scénario : fichier distinct, schéma appliqué par les <b>migrations</b>
    /// réelles, puis initialisation Production sûre. Renvoie le chemin du fichier.
    /// </summary>
    protected string CreateMigratedDatabase(string fileName = "acceptance.db")
    {
        var path = Path.Combine(_workDirectory, fileName);

        using (var context = CreateContext(path))
        {
            // Migrations RÉELLES : __EFMigrationsHistory est renseignée et le SQL personnalisé des migrations
            // (dont l'index unique filtré P3-8) est réellement exécuté.
            context.Database.Migrate();

            // Chemin de seed Production sûr, identique à celui du démarrage desktop. Le défaut de SeedOptions est
            // Production / sans seed de démonstration / sans secret bootstrap : le compte « admin » faible inséré
            // par la migration InitialCreate est donc NEUTRALISÉ (désactivé), et aucune donnée de démonstration
            // n'est écrite. Les options sont construites directement, sans toucher aux variables d'environnement
            // du processus.
            new DatabaseSeeder().Seed(context, new SeedOptions());
        }

        return path;
    }

    /// <summary>
    /// Ouvre un <see cref="OpticDbContext"/> sur la base indiquée. Clés étrangères activées, pooling désactivé.
    /// </summary>
    protected static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False;Foreign Keys=True";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>
    /// Ouvre une portée d'actes : un <see cref="OpticDbContext"/> neuf et les use cases composés autour de lui.
    /// Chaque portée est libérée à la sortie du <c>using</c>, exactement comme une portée DI de production.
    /// </summary>
    protected static AcceptanceScope OpenScope(string databasePath) => OpenScopeFor(databasePath);

    /// <summary>Variante accessible aux helpers de scénario (<see cref="AcceptanceActs"/>).</summary>
    internal static AcceptanceScope OpenScopeFor(string databasePath) => new(CreateContext(databasePath));

    /// <summary>
    /// Ouvre un contexte de <b>lecture</b> neuf sur la même base. Indispensable : toutes les primitives atomiques
    /// (<c>TryTransitionStatusAsync</c>, <c>TrySettleRemainingBalanceAsync</c>, <c>DecrementStockAsync</c>,
    /// <c>TryAcquireActiveAsync</c>) passent par <c>ExecuteUpdateAsync</c> et ne mettent <b>pas</b> à jour le
    /// change tracker — relire depuis un contexte d'acte renverrait des valeurs périmées.
    /// </summary>
    protected static OpticDbContext OpenFreshRead(string databasePath) => CreateContext(databasePath);
}

/// <summary>
/// Portée d'exécution d'un scénario : un <see cref="OpticDbContext"/> et les use cases composés autour de lui.
/// Reproduit la portée <c>AddScoped</c> de production (un contexte partagé par tous les dépôts d'une portée) sans
/// aucun conteneur d'injection de dépendances.
/// </summary>
/// <remarks>
/// Les dépendances de notification, <b>optionnelles</b> en production (<c>INotificationRepository?</c>), sont
/// fournies <b>explicitement</b> : le parcours de recette doit créer les notifications de statut et de paiement
/// comme le fait l'application réelle. Les laisser à <c>null</c> observerait un parcours amputé.
/// </remarks>
public sealed class AcceptanceScope : IAsyncDisposable
{
    public AcceptanceScope(OpticDbContext context)
    {
        Context = context;

        var unitOfWork = new UnitOfWork(context);
        var transactionRunner = new EfTransactionRunner(context);
        var stockMutationService = new EfStockMutationService(context);
        var numberSequenceService = new EfNumberSequenceService(context);

        var saleRepository = new SaleRepository(context);
        var orderRepository = new OrderRepository(context);
        var productRepository = new ProductRepository(context);
        var customerRepository = new CustomerRepository(context);
        var supplierRepository = new SupplierRepository(context);
        var prescriptionRepository = new PrescriptionRepository(context);
        var stockMovementRepository = new StockMovementRepository(context);
        var notificationRepository = new NotificationRepository(context);

        CreateSupplier = new CreateSupplierUseCase(supplierRepository, unitOfWork);
        CreateProduct = new CreateProductUseCase(productRepository, supplierRepository, unitOfWork, transactionRunner);
        SetProductActive = new SetProductActiveUseCase(productRepository, unitOfWork);
        CreateCustomer = new CreateCustomerUseCase(customerRepository, unitOfWork);
        SetCustomerArchived = new SetCustomerArchivedUseCase(customerRepository, unitOfWork);
        CreatePrescription = new CreatePrescriptionUseCase(prescriptionRepository, customerRepository, unitOfWork);

        RegisterSale = new RegisterSaleUseCase(
            saleRepository,
            orderRepository,
            productRepository,
            customerRepository,
            stockMovementRepository,
            unitOfWork,
            transactionRunner,
            stockMutationService,
            numberSequenceService,
            SystemClock.Instance);

        AdvanceOrderStatus = new AdvanceOrderStatusUseCase(
            orderRepository,
            stockMovementRepository,
            unitOfWork,
            transactionRunner,
            stockMutationService,
            SystemClock.Instance,
            notificationRepository);

        SettleOrderBalance = new SettleOrderBalanceUseCase(
            orderRepository,
            saleRepository,
            unitOfWork,
            transactionRunner,
            SystemClock.Instance,
            notificationRepository);

        GetCurrentWorkshopSheet = new GetCurrentWorkshopSheetUseCase(orderRepository);
        ValidateWorkshopSheetQc = new ValidateWorkshopSheetQcUseCase(orderRepository, transactionRunner);

        CreateStockMovement = new CreateStockMovementUseCase(
            stockMovementRepository,
            productRepository,
            unitOfWork,
            transactionRunner,
            stockMutationService);
    }

    public OpticDbContext Context { get; }

    public ICreateSupplierUseCase CreateSupplier { get; }
    public ICreateProductUseCase CreateProduct { get; }
    public ISetProductActiveUseCase SetProductActive { get; }
    public ICreateCustomerUseCase CreateCustomer { get; }
    public ISetCustomerArchivedUseCase SetCustomerArchived { get; }
    public ICreatePrescriptionUseCase CreatePrescription { get; }
    public IRegisterSaleUseCase RegisterSale { get; }
    public IAdvanceOrderStatusUseCase AdvanceOrderStatus { get; }
    public ISettleOrderBalanceUseCase SettleOrderBalance { get; }
    public IGetCurrentWorkshopSheetUseCase GetCurrentWorkshopSheet { get; }
    public IValidateWorkshopSheetQcUseCase ValidateWorkshopSheetQc { get; }
    public ICreateStockMovementUseCase CreateStockMovement { get; }

    /// <summary>Libère la portée — et donc le contexte partagé — exactement comme une portée DI de production.</summary>
    public ValueTask DisposeAsync() => Context.DisposeAsync();
}

/// <summary>
/// Constantes métier partagées par les scénarios de recette. Chaque valeur porte l'intention qui l'a choisie :
/// aucun nombre magique n'apparaît dans le corps des tests.
/// </summary>
public static class AcceptanceData
{
    public const string SupplierName = "Optiverre Distribution";

    public const string CustomerFirstName = "Camille";
    public const string CustomerLastName = "Berger";

    public const string FrameReference = "MON-1001";
    public const string LensOdReference = "VER-OD-2001";
    public const string LensOgReference = "VER-OG-2002";

    /// <summary>Stock monture : décrémenté à la VENTE. 5 > seuil 2, pour qu'aucune alerte stock bas n'apparaisse.</summary>
    public const int FrameInitialStock = 5;
    public const int FrameAlertThreshold = 2;

    /// <summary>Stock verre : décrémenté à la FABRICATION. 4 > seuil 1, même intention.</summary>
    public const int LensInitialStock = 4;
    public const int LensAlertThreshold = 1;

    public const decimal FramePrice = 120.00m;
    public const decimal LensPrice = 95.00m;

    public const decimal Discount = 10.00m;
    public const decimal Deposit = 100.00m;

    /// <summary>Total attendu = monture + 2 verres. Exprimé à partir des entrées, jamais recopié en dur.</summary>
    public const decimal ExpectedTotal = FramePrice + (2 * LensPrice);
    public const decimal ExpectedFinal = ExpectedTotal - Discount;
    public const decimal ExpectedRemaining = ExpectedFinal - Deposit;

    // --- Valeurs optiques de l'ordonnance (axe présent car cylindre non nul : invariant PrescriptionValidator).
    public const double OdSphere = 2.00;
    public const double OdCylinder = -1.00;
    public const int OdAxis = 180;
    public const double OdAddition = 2.50;

    public const double OgSphere = 1.75;
    public const double OgCylinder = -0.75;
    public const int OgAxis = 90;
}

/// <summary>
/// Helpers de scénario. Chaque helper représente <b>au plus un acte métier</b> et passe exclusivement par un use
/// case public : aucune mutation directe par <c>DbContext</c> ou par dépôt, aucun recalcul de montant, aucune
/// duplication de <c>SalePricingPolicy</c>, de la machine à états ou de l'empreinte atelier.
/// </summary>
public static class AcceptanceActs
{
    public static async Task<long> CreerFournisseurAsync(AcceptanceScope scope, string name = AcceptanceData.SupplierName)
    {
        var result = await scope.CreateSupplier.ExecuteAsync(new CreateSupplierCommand { Name = name });
        result.IsValid.Should().BeTrue("le fournisseur nominal doit être accepté");
        return result.SupplierId;
    }

    public static async Task<long> CreerProduitAsync(
        AcceptanceScope scope,
        long supplierId,
        string reference,
        string name,
        ProductCategoryEnum category,
        int stock,
        int threshold,
        decimal salePrice)
    {
        var result = await scope.CreateProduct.ExecuteAsync(new CreateProductCommand
        {
            Reference = reference,
            Name = name,
            Category = category,
            SupplierId = supplierId,
            PurchasePrice = salePrice / 2m,
            SalePrice = salePrice,
            StockQuantity = stock,
            StockAlertThreshold = threshold
        });

        result.IsValid.Should().BeTrue("le produit « {0} » doit être accepté", reference);
        return result.ProductId;
    }

    public static async Task<long> CreerClientAsync(
        AcceptanceScope scope,
        string firstName = AcceptanceData.CustomerFirstName,
        string lastName = AcceptanceData.CustomerLastName)
    {
        var result = await scope.CreateCustomer.ExecuteAsync(new CreateCustomerCommand
        {
            FirstName = firstName,
            LastName = lastName
        });

        result.IsValid.Should().BeTrue("le client nominal doit être accepté");
        return result.CustomerId;
    }

    public static async Task<long> CreerOrdonnanceAsync(AcceptanceScope scope, long customerId, DateOnly issueDate)
    {
        var result = await scope.CreatePrescription.ExecuteAsync(new CreatePrescriptionCommand
        {
            CustomerId = customerId,
            IssueDate = issueDate,
            OdSphere = AcceptanceData.OdSphere,
            OdCylinder = AcceptanceData.OdCylinder,
            OdAxis = AcceptanceData.OdAxis,
            OdAddition = AcceptanceData.OdAddition,
            OgSphere = AcceptanceData.OgSphere,
            OgCylinder = AcceptanceData.OgCylinder,
            OgAxis = AcceptanceData.OgAxis
        });

        result.CustomerFound.Should().BeTrue();
        result.IsValid.Should().BeTrue("l'ordonnance nominale doit être acceptée");
        return result.PrescriptionId;
    }

    /// <summary>
    /// Construit la commande de vente nominale (monture + 2 verres). <b>Ne l'exécute pas</b> : l'acte reste
    /// visible dans le corps du scénario.
    /// </summary>
    public static RegisterSaleCommand VenteLunettesCompletes(long? customerId, long frameId, long lensOdId, long lensOgId) =>
        new()
        {
            CustomerId = customerId,
            DiscountAmount = AcceptanceData.Discount,
            DepositAmount = AcceptanceData.Deposit,
            PaymentMethod = PaymentMethod.Card,
            Lines = new List<RegisterSaleLineCommand>
            {
                new()
                {
                    ProductId = frameId,
                    ItemType = OrderItemType.Frame,
                    Quantity = 1,
                    UnitPrice = AcceptanceData.FramePrice
                },
                new()
                {
                    ProductId = lensOdId,
                    ItemType = OrderItemType.LensOd,
                    Quantity = 1,
                    UnitPrice = AcceptanceData.LensPrice,
                    Sphere = AcceptanceData.OdSphere,
                    Cylinder = AcceptanceData.OdCylinder,
                    Axis = AcceptanceData.OdAxis,
                    Addition = AcceptanceData.OdAddition
                },
                new()
                {
                    ProductId = lensOgId,
                    ItemType = OrderItemType.LensOg,
                    Quantity = 1,
                    UnitPrice = AcceptanceData.LensPrice,
                    Sphere = AcceptanceData.OgSphere,
                    Cylinder = AcceptanceData.OgCylinder,
                    Axis = AcceptanceData.OgAxis
                }
            }
        };

    /// <summary>Construit une transition unique. Les libellés d'affichage sont fournis par l'appelant, comme en production.</summary>
    public static AdvanceOrderStatusCommand Transition(long orderId, OrderStatus from, OrderStatus to) =>
        new()
        {
            OrderId = orderId,
            CurrentStatus = from,
            NextStatus = to,
            CustomerDisplayName = $"{AcceptanceData.CustomerFirstName} {AcceptanceData.CustomerLastName}",
            CurrentStatusDisplay = from.ToString(),
            NextStatusDisplay = to.ToString()
        };

    /// <summary>
    /// Exécute <b>une</b> transition de statut dans sa <b>propre portée</b>, comme le fait la production : en
    /// desktop, chaque action utilisateur ouvre une portée DI neuve, donc un <c>OpticDbContext</c> neuf.
    /// </summary>
    /// <remarks>
    /// <b>Cette portée par transition n'est pas un confort de test : elle est nécessaire.</b> À la fin d'une
    /// transition réussie, <c>AdvanceOrderStatusUseCase</c> réaligne l'entité renvoyée à la ViewModel
    /// (<c>fresh.Status = nextStatus</c>, <c>AdvanceOrderStatusUseCase.cs:193</c>). Cette affectation porte sur une
    /// entité <b>suivie</b> et laisse donc la propriété <c>Status</c> marquée « modifiée » dans le change tracker.
    /// Enchaîner une seconde transition dans le <b>même</b> <c>DbContext</c> ferait réécrire cette valeur périmée
    /// par le <c>SaveChangesAsync</c> de la seconde transition, annulant la prise atomique qui venait de réussir.
    /// Vérifié empiriquement en P3-11 : deux transitions enchaînées dans une même portée échouent sur
    /// <c>OrderStatusConflictException</c>. Le comportement est correct portée par portée ; la limite est
    /// documentée comme dette (rapport P3-11 §« Limites du modèle »), et aucun code de production n'est modifié.
    /// </remarks>
    public static async Task<AdvanceOrderStatusResult> AvancerStatutAsync(
        string databasePath,
        long orderId,
        OrderStatus from,
        OrderStatus to)
    {
        await using var scope = AcceptanceScenarioBase.OpenScopeFor(databasePath);
        return await scope.AdvanceOrderStatus.ExecuteAsync(Transition(orderId, from, to));
    }
}
