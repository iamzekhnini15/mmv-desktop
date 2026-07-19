using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;

namespace MMV.Application.Tests.UseCases.WorkshopSheets;

/// <summary>
/// Socle commun des tests P3-6B : vrai SQLite temporaire (jamais le provider InMemory), schéma réel, et jeu de
/// données minimal représentatif d'une commande d'atelier (monture + verre OD complet).
/// </summary>
/// <remarks>
/// Enforcement des clés étrangères <b>désactivé</b>, comme pour <c>AdvanceOrderStatusUseCaseTests</c> et
/// <c>CreateOrderUseCaseTests</c> : le flux Commandes manipule des commandes dont <c>SaleId</c> peut être
/// orphelin (défaut préexistant et orthogonal, cf. rapport P2B-2D §16.2). Il s'agit toujours de vrai SQLite.
/// </remarks>
public abstract class WorkshopSheetTestBase : IDisposable
{
    private readonly string _workDirectory;

    protected WorkshopSheetTestBase()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p36b-" + Guid.NewGuid().ToString("N"));
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

        GC.SuppressFinalize(this);
    }

    protected string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    protected static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False;Foreign Keys=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    protected static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Crée une commande complète : client, vente parente, produits (monture + verre) et deux lignes, dont une
    /// ligne verre portant l'intégralité des données optiques (y compris usage, prisme, base et acuité).
    /// </summary>
    protected static long SeedOrder(
        string databasePath,
        OrderStatus status,
        string orderNumber = "CMD-000100",
        string? notes = "Montage standard")
    {
        using var context = CreateContext(databasePath);

        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var frame = new Product
        {
            Reference = "MON-001",
            Name = "Monture Alpha",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 20m,
            SalePrice = 60m,
            StockQuantity = 10
        };
        var lens = new Product
        {
            Reference = "VER-001",
            Name = "Verre Ultra",
            Category = ProductCategoryEnum.VERRE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 40m,
            StockQuantity = 10
        };
        context.Products.AddRange(frame, lens);
        context.SaveChanges();

        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            Phone = "0600000000",
            Email = "jean.dupont@example.test"
        };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale
        {
            SaleNumber = "VTE-000001",
            CustomerId = customer.CustomerId,
            FinalAmount = 100m
        };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            OrderNumber = orderNumber,
            SaleId = sale.SaleId,
            OrderDate = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
            EstimatedDelivery = new DateTime(2026, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            Notes = notes,
            OrderItems =
            {
                new OrderItem
                {
                    ProductId = frame.ProductId,
                    ItemType = OrderItemType.Frame,
                    Quantity = 1,
                    UnitPrice = 60m
                },
                new OrderItem
                {
                    ProductId = lens.ProductId,
                    ItemType = OrderItemType.LensOd,
                    Quantity = 1,
                    UnitPrice = 40m,
                    UsageType = LensUsageType.Progressive,
                    Sphere = 2.00,
                    Cylinder = -1.00,
                    Axis = 180,
                    Addition = 2.50,
                    PrismValue = 1.50,
                    PrismBase = PrismBase.Out,
                    VisualAcuity = "10/10"
                }
            }
        };
        context.Orders.Add(order);
        context.SaveChanges();

        return order.OrderId;
    }

    /// <summary>
    /// Crée une commande sans aucune ligne (cas de refus métier). La vente parente est <b>indispensable</b> :
    /// <c>GetWithItemsAsync</c> inclut la navigation requise <c>Order → Sale</c> (INNER JOIN), donc une commande
    /// orpheline serait introuvable et le test observerait « commande introuvable » au lieu du refus visé.
    /// </summary>
    protected static long SeedOrderWithoutItems(string databasePath, OrderStatus status)
    {
        using var context = CreateContext(databasePath);

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale { SaleNumber = "VTE-000900", CustomerId = customer.CustomerId, FinalAmount = 0m };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            OrderNumber = "CMD-000900",
            SaleId = sale.SaleId,
            OrderDate = DateTime.UtcNow,
            Status = status
        };
        context.Orders.Add(order);
        context.SaveChanges();

        return order.OrderId;
    }

    /// <summary>Modifie une donnée technique de la commande, rendant toute fiche existante obsolète.</summary>
    protected static void ChangeOrderTechnicalData(string databasePath, long orderId)
    {
        using var context = CreateContext(databasePath);
        var item = context.OrderItems.First(i => i.OrderId == orderId && i.ItemType == OrderItemType.LensOd);
        item.Sphere = 3.00;
        context.SaveChanges();
    }
}
