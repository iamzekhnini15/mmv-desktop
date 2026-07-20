using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P3-8 — Non-régression des notifications <b>événementielles</b> (transitions de commande).
///
/// <para>
/// P3-8 n'a modifié dans <c>AdvanceOrderStatusUseCase</c> que le remplacement de deux chaînes littérales par des
/// constantes Domain. Ces tests verrouillent ce que l'ajout de l'index unique filtré aurait pu casser sans qu'aucun
/// test existant ne s'en aperçoive : <b>plusieurs transitions d'une même commande doivent rester possibles</b>, et
/// un rejeu ne doit produire aucune notification fantôme.
/// </para>
///
/// <para>
/// Les garanties symétriques du règlement de solde (une notification par encaissement, aucune sur rejeu) sont déjà
/// couvertes par <c>SettleOrderBalanceAtomicityTests</c> et restent vertes sans modification.
/// </para>
/// </summary>
public sealed class EventNotificationsNonRegressionTests : IDisposable
{
    private readonly string _workDirectory;

    public EventNotificationsNonRegressionTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p38-events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    // Enforcement FK relâché, comme AdvanceOrderStatusUseCaseTests (commandes autonomes, dette préexistante et
    // orthogonale). Toujours du vrai SQLite : fichier et schéma réels.
    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False;Foreign Keys=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static AdvanceOrderStatusUseCase CreateUseCase(OpticDbContext context)
        => new(
            new OrderRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            new NotificationRepository(context));

    private long SeedOrder(string databasePath, out long productId)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();

        var supplier = new Supplier { Name = "Fournisseur" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = "VER-100",
            Name = "Verre",
            Category = ProductCategoryEnum.VERRE,
            SupplierId = supplier.SupplierId,
            StockQuantity = 50,
            StockAlertThreshold = 5,
        };
        context.Products.Add(product);
        context.SaveChanges();
        productId = product.ProductId;

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale { SaleNumber = "VTE-000001", CustomerId = customer.CustomerId, FinalAmount = 100m };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            OrderNumber = "CMD-000200",
            SaleId = sale.SaleId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.New,
            OrderItems =
            {
                new OrderItem { ProductId = productId, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 80m },
            },
        };
        context.Orders.Add(order);
        context.SaveChanges();
        return order.OrderId;
    }

    private static AdvanceOrderStatusCommand CommandFor(long orderId, OrderStatus current, OrderStatus next)
        => new()
        {
            OrderId = orderId,
            CurrentStatus = current,
            NextStatus = next,
            CustomerDisplayName = "Jean Dupont",
            CurrentStatusDisplay = current.ToString(),
            NextStatusDisplay = next.ToString(),
        };

    [Fact]
    public async Task PlusieursTransitionsDUneMemeCommande_RestentAutorisees()
    {
        // Preuve directe que l'index unique filtré ne déborde pas sur les faits historiques : deux
        // OrderStatusChanged portent le MÊME (Type, EntityType, EntityId). Sans la restriction « Type = 'LowStock' »
        // dans le filtre, la seconde transition serait refusée par la base.
        var dbPath = PathFor("multi-transitions.db");
        var orderId = SeedOrder(dbPath, out _);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(CommandFor(orderId, OrderStatus.New, OrderStatus.ToFabricate));
        }

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(CommandFor(orderId, OrderStatus.ToFabricate, OrderStatus.InProgress));
        }

        using var verify = CreateContext(dbPath);
        var notifications = verify.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationTypes.OrderStatusChanged)
            .ToList();

        notifications.Should().HaveCount(2, "une commande traverse plusieurs étapes, toutes légitimes");
        notifications.Should().OnlyContain(n => n.EntityId == orderId);
        notifications.Should().OnlyContain(n => n.EntityType == NotificationEntityTypes.Order);
        notifications.Should().OnlyContain(n => n.ResolvedAt == null, "un fait historique n'est jamais résolu");
    }

    [Fact]
    public async Task RejeuDUneTransitionDejaConsommee_NeCreeAucuneNotificationSupplementaire()
    {
        var dbPath = PathFor("retry-transition.db");
        var orderId = SeedOrder(dbPath, out _);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(CommandFor(orderId, OrderStatus.New, OrderStatus.ToFabricate));
        }

        // Rejeu : le CAS de statut ne trouve plus « New » et refuse AVANT d'atteindre le bloc de notification.
        using (var context = CreateContext(dbPath))
        {
            var replay = async () => await CreateUseCase(context)
                .ExecuteAsync(CommandFor(orderId, OrderStatus.New, OrderStatus.ToFabricate));

            await replay.Should().ThrowAsync<OrderStatusConflictException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.Type == NotificationTypes.OrderStatusChanged)
            .Should().Be(1, "un rejeu ne crée jamais de notification fantôme");
    }

    [Fact]
    public async Task LesConstantesDomain_ProduisentExactementLesMemesValeursQuAvantP38()
    {
        // Le remplacement des littéraux par des constantes doit être STRICTEMENT neutre en base : toute divergence
        // casserait silencieusement l'anti-doublon et les filtres de l'index.
        var dbPath = PathFor("constants.db");
        var orderId = SeedOrder(dbPath, out _);

        using (var context = CreateContext(dbPath))
        {
            await CreateUseCase(context).ExecuteAsync(CommandFor(orderId, OrderStatus.New, OrderStatus.ToFabricate));
        }

        using var verify = CreateContext(dbPath);
        var notification = verify.Notifications.AsNoTracking().Single();

        notification.Type.Should().Be("OrderStatusChanged");
        notification.EntityType.Should().Be("Order");

        NotificationTypes.LowStock.Should().Be("LowStock");
        NotificationTypes.StockOut.Should().Be("StockOut");
        NotificationTypes.OrderStatusChanged.Should().Be("OrderStatusChanged");
        NotificationTypes.PaymentReceived.Should().Be("PaymentReceived");
        NotificationTypes.Info.Should().Be("Info");
        NotificationEntityTypes.Product.Should().Be("Product");
        NotificationEntityTypes.Order.Should().Be("Order");
    }
}
