using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2B-2E — Troisième vertical slice : use case « Faire avancer le statut / réception d'une commande »
/// (<see cref="AdvanceOrderStatusUseCase"/>), extrait iso-fonctionnellement de
/// <c>OrderDetailViewModel.AdvanceStatusAsync</c>.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>avance le statut nominal (New → ToFabricate), persiste le statut et crée la notification ;</item>
///   <item>au passage À fabriquer → En fabrication, crée un mouvement de stock <c>Out</c> par article (quantité
///         positive, motif « Fabrication commande … ») et décrémente le stock produit ;</item>
///   <item>n'émet aucune notification quand le repository de notifications est absent (<c>null</c>) ;</item>
///   <item>renvoie <c>OrderFound = false</c> sans écrire si la commande est introuvable ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class AdvanceOrderStatusUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public AdvanceOrderStatusUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
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
            // Nettoyage best-effort.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    // NB — Enforcement des clés étrangères DÉSACTIVÉ ("Foreign Keys=False"), comme pour CreateOrderUseCaseTests.
    //
    // Le flux Commandes manipule des commandes autonomes (Order.SaleId = 0, problème préexistant et orthogonal,
    // cf. rapport P2B-2D §16.2). On relâche l'enforcement FK pour isoler et valider le contrat réel du use case
    // (avancement de statut, mouvements de stock, décrément, notification) indépendamment de ce problème latent.
    // Il s'agit toujours de vrai SQLite (fichier + schéma réels), jamais du provider InMemory.
    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False;Foreign Keys=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Construit le use case réel au-dessus d'un unique contexte (DbContext partagé).</summary>
    private static AdvanceOrderStatusUseCase CreateUseCase(OpticDbContext context, bool withNotifications = true)
    {
        return new AdvanceOrderStatusUseCase(
            new OrderRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            withNotifications ? new NotificationRepository(context) : null);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedProduct(string databasePath, int stockQuantity)
    {
        using var context = CreateContext(databasePath);
        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = "VER-001",
            Name = "Verre Test",
            Category = ProductCategoryEnum.VERRE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = stockQuantity,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    /// <summary>
    /// Crée une vente parente (avec client) puis une commande liée, avec un statut donné et un article
    /// (optionnellement lié à un produit). La vente parente est nécessaire car <c>GetWithItemsAsync</c> inclut la
    /// navigation requise <c>Order → Sale</c> (INNER JOIN) : en production une commande est toujours liée à une
    /// vente (créée par le flux d'enregistrement de vente).
    /// </summary>
    private static long SeedOrder(string databasePath, OrderStatus status, string orderNumber, long? productId, int quantity)
    {
        using var context = CreateContext(databasePath);

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale
        {
            SaleNumber = "VTE-000001",
            CustomerId = customer.CustomerId,
            FinalAmount = 100m,
        };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            OrderNumber = orderNumber,
            SaleId = sale.SaleId,
            OrderDate = DateTime.UtcNow,
            Status = status,
            OrderItems =
            {
                new OrderItem
                {
                    ProductId = productId,
                    ItemType = OrderItemType.LensOd,
                    Quantity = quantity,
                    UnitPrice = 80m,
                }
            }
        };
        context.Orders.Add(order);
        context.SaveChanges();
        return order.OrderId;
    }

    // ------------------------------------------------------------------
    // (1) Avancement nominal : New → ToFabricate (pas de stock), notification créée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NominalAdvance_UpdatesStatus_AndCreatesNotification_WithoutStockMovements()
    {
        var dbPath = PathFor("nominal.db");
        EnsureSchema(dbPath);
        var lensId = SeedProduct(dbPath, stockQuantity: 5);
        var orderId = SeedOrder(dbPath, OrderStatus.New, "CMD-000100", lensId, quantity: 1);

        AdvanceOrderStatusResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.New,
                NextStatus = OrderStatus.ToFabricate,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "Nouveau",
                NextStatusDisplay = "À fabriquer",
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.OrderFound.Should().BeTrue();
        result.Order.Should().NotBeNull();
        result.OldStatus.Should().Be(OrderStatus.New);
        result.NewStatus.Should().Be(OrderStatus.ToFabricate);
        result.HasCreatedStockMovements.Should().BeFalse("New → ToFabricate ne déclenche pas la sortie de stock");
        result.CreatedStockMovementCount.Should().Be(0);
        result.HasNotification.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.ToFabricate);
        verify.StockMovements.AsNoTracking().Should().BeEmpty();
        var notification = verify.Notifications.AsNoTracking().Single();
        notification.Type.Should().Be("OrderStatusChanged");
        notification.Title.Should().Be("Commande CMD-000100 : À fabriquer");
        notification.Message.Should().Be("La commande CMD-000100 (Jean Dupont) est passée de 'Nouveau' à 'À fabriquer'.");
        notification.EntityId.Should().Be(orderId);
        notification.EntityType.Should().Be("Order");
        notification.IsRead.Should().BeFalse();
        verify.Products.AsNoTracking().Single(p => p.ProductId == lensId).StockQuantity
            .Should().Be(5, "aucune sortie de stock à cette transition");
    }

    // ------------------------------------------------------------------
    // (2) Passage en fabrication : mouvement de stock Out + décrément produit
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ToFabricateToInProgress_CreatesStockMovement_AndDecrementsStock()
    {
        var dbPath = PathFor("fabrication.db");
        EnsureSchema(dbPath);
        var lensId = SeedProduct(dbPath, stockQuantity: 5);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate, "CMD-000200", lensId, quantity: 2);

        AdvanceOrderStatusResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.ToFabricate,
                NextStatus = OrderStatus.InProgress,
                CustomerDisplayName = "Client inconnu",
                CurrentStatusDisplay = "À fabriquer",
                NextStatusDisplay = "En fabrication",
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.HasCreatedStockMovements.Should().BeTrue();
        result.CreatedStockMovementCount.Should().Be(1);
        result.NewStatus.Should().Be(OrderStatus.InProgress);

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.InProgress);

        var movement = verify.StockMovements.AsNoTracking().Single();
        movement.ProductId.Should().Be(lensId);
        movement.MovementType.Should().Be(StockMovementType.Out);
        movement.Quantity.Should().Be(-2, "sortie ⇒ delta négatif (convention de signe P3-5)");
        movement.Reason.Should().Be("Fabrication commande CMD-000200");

        verify.Products.AsNoTracking().Single(p => p.ProductId == lensId).StockQuantity
            .Should().Be(3, "le stock produit est décrémenté de la quantité commandée (5 - 2)");
    }

    // ------------------------------------------------------------------
    // (3) Sans repository de notifications : aucune notification créée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithoutNotificationRepository_DoesNotCreateNotification()
    {
        var dbPath = PathFor("no-notif.db");
        EnsureSchema(dbPath);
        var lensId = SeedProduct(dbPath, stockQuantity: 5);
        var orderId = SeedOrder(dbPath, OrderStatus.New, "CMD-000300", lensId, quantity: 1);

        AdvanceOrderStatusResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context, withNotifications: false);
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.New,
                NextStatus = OrderStatus.ToFabricate,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "Nouveau",
                NextStatusDisplay = "À fabriquer",
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.HasNotification.Should().BeFalse();

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Should().BeEmpty();
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.ToFabricate,
            "le statut avance même sans notification");
    }

    // ------------------------------------------------------------------
    // (4) Commande introuvable : OrderFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ReturnsNotFound_AndWritesNothing()
    {
        var dbPath = PathFor("notfound.db");
        EnsureSchema(dbPath);

        AdvanceOrderStatusResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = 99999,
                CurrentStatus = OrderStatus.New,
                NextStatus = OrderStatus.ToFabricate,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "Nouveau",
                NextStatusDisplay = "À fabriquer",
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.OrderFound.Should().BeFalse();
        result.Order.Should().BeNull();

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Should().BeEmpty();
        verify.StockMovements.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (5) Commande nulle → ArgumentNullException
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = CreateUseCase(context);

        Func<Task> act = () => useCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (6) Dépendance critique manquante → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutOrderRepository_Throws()
    {
        var dbPath = PathFor("ctor.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new AdvanceOrderStatusUseCase(
            orderRepository: null!,
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context));

        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (7) P3-5 — Fabrication avec stock insuffisant : erreur contrôlée, rollback TOTAL
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Fabrication_InsufficientStock_Throws_AndRollsBackEverything()
    {
        var dbPath = PathFor("insufficient.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, stockQuantity: 1);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate, "CMD-000400", productId, quantity: 2);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.ToFabricate,
                NextStatus = OrderStatus.InProgress,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "À fabriquer",
                NextStatusDisplay = "En fabrication",
            };

            Func<Task> act = () => useCase.ExecuteAsync(command);
            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status
            .Should().Be(OrderStatus.ToFabricate, "statut inchangé : la prise atomique est annulée par le rollback");
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(1, "stock inchangé, jamais négatif");
        verify.StockMovements.AsNoTracking().Should().BeEmpty("aucun mouvement conservé");
        verify.Notifications.AsNoTracking().Should().BeEmpty("aucune notification persistée");
    }

    // ------------------------------------------------------------------
    // (8) P3-5 — Répétition de la même transition : refus contrôlé, PAS de double décrément
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RepeatedFabricationTransition_IsRefused_NoDoubleDecrement()
    {
        var dbPath = PathFor("repeat.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, stockQuantity: 5);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate, "CMD-000500", productId, quantity: 2);

        AdvanceOrderStatusCommand Command() => new()
        {
            OrderId = orderId,
            CurrentStatus = OrderStatus.ToFabricate,
            NextStatus = OrderStatus.InProgress,
            CustomerDisplayName = "Jean Dupont",
            CurrentStatusDisplay = "À fabriquer",
            NextStatusDisplay = "En fabrication",
        };

        // Première exécution : succès (stock 5 → 3, un mouvement).
        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(Command());
        }

        // Deuxième exécution AVEC LE MÊME statut attendu (ToFabricate) : le statut stocké est désormais InProgress
        // ⇒ la prise atomique échoue ⇒ conflit contrôlé, aucun re-décrément.
        using (var context = CreateContext(dbPath))
        {
            Func<Task> act = () => CreateUseCase(context).ExecuteAsync(Command());
            await act.Should().ThrowAsync<OrderStatusConflictException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(3, "le stock est décrémenté UNE SEULE fois (5 - 2), pas de double décrément");
        verify.StockMovements.AsNoTracking().Count(m => m.ProductId == productId)
            .Should().Be(1, "un seul mouvement de fabrication au total");
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status
            .Should().Be(OrderStatus.InProgress);
    }

    // ------------------------------------------------------------------
    // (9) P3-5 — Deux exécutions concurrentes : jamais de double décrément (invariant de sûreté)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TwoConcurrentFabricationTransitions_NeverDoubleDecrements()
    {
        var dbPath = PathFor("concurrent.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, stockQuantity: 5);
        var orderId = SeedOrder(dbPath, OrderStatus.ToFabricate, "CMD-000600", productId, quantity: 2);

        async Task<bool> TryAdvanceAsync()
        {
            using var context = CreateContext(dbPath);
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = orderId,
                CurrentStatus = OrderStatus.ToFabricate,
                NextStatus = OrderStatus.InProgress,
                CustomerDisplayName = "Jean Dupont",
                CurrentStatusDisplay = "À fabriquer",
                NextStatusDisplay = "En fabrication",
            };
            try
            {
                await CreateUseCase(context).ExecuteAsync(command);
                return true;
            }
            catch (Exception ex) when (ex is OrderStatusConflictException or InsufficientStockException or PersistenceException)
            {
                return false; // refus contrôlé (conflit de statut, stock, ou contention persistante)
            }
        }

        var results = await Task.WhenAll(TryAdvanceAsync(), TryAdvanceAsync());

        results.Count(success => success).Should().BeLessThanOrEqualTo(1,
            "au plus une des deux exécutions concurrentes peut prendre la transition");

        using var verify = CreateContext(dbPath);
        var finalStock = verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity;
        finalStock.Should().BeOneOf(new[] { 3, 5 },
            "le stock est décrémenté AU PLUS une fois (jamais 1 = double décrément)");
        verify.StockMovements.AsNoTracking().Count(m => m.ProductId == productId)
            .Should().BeLessThanOrEqualTo(1, "au plus un mouvement de fabrication (jamais deux)");
    }
}
