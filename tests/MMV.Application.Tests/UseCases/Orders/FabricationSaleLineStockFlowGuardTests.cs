using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P3-11 — garde de classification opposée par <see cref="AdvanceOrderStatusUseCase"/> à l'entrée en fabrication.
/// </summary>
/// <remarks>
/// <para>
/// La garde de <c>RegisterSaleUseCase</c> ne protège que les ventes enregistrées <b>après</b> P3-11. Elle ne dit
/// rien des commandes <b>déjà persistées</b> — historiques, créées par un autre chemin d'écriture
/// (<c>CreateOrderUseCase</c> / <c>UpdateOrderUseCase</c>, non modifiés par P3-11), ou altérées en base. Ces
/// tests arrangent donc <b>directement</b> l'état incohérent, comme le ferait une donnée héritée, et prouvent
/// que le double décrément ne peut plus se produire.
/// </para>
/// </remarks>
public sealed class FabricationSaleLineStockFlowGuardTests : IDisposable
{
    private readonly string _workDirectory;

    public FabricationSaleLineStockFlowGuardTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p3-11-fab-" + Guid.NewGuid().ToString("N"));
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

    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static AdvanceOrderStatusUseCase CreateUseCase(OpticDbContext context)
        => new(
            new OrderRepository(context),
            new StockMovementRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context),
            SystemClock.Instance,
            new NotificationRepository(context));

    private const int StockInitial = 7;

    /// <summary>
    /// Arrange <b>direct</b> d'un état historique : vente valide, commande en <c>ToFabricate</c>, et un article
    /// dont le type contredit (ou non) la catégorie du produit.
    /// </summary>
    private static (long OrderId, long ProductId) SeedOrder(
        string databasePath,
        ProductCategoryEnum category,
        OrderItemType itemType,
        string reference)
    {
        using var context = CreateContext(databasePath);

        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = reference,
            Name = "Produit " + reference,
            Category = category,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = StockInitial,
            StockAlertThreshold = 1,
            IsActive = true,
        };
        context.Products.Add(product);

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
            OrderNumber = "CMD-000001",
            SaleId = sale.SaleId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.ToFabricate,
            OrderItems =
            {
                new OrderItem
                {
                    ProductId = product.ProductId,
                    ItemType = itemType,
                    Quantity = 1,
                    UnitPrice = 80m,
                }
            }
        };
        context.Orders.Add(order);
        context.SaveChanges();

        return (order.OrderId, product.ProductId);
    }

    private static AdvanceOrderStatusCommand EntrerEnFabrication(long orderId) => new()
    {
        OrderId = orderId,
        CurrentStatus = OrderStatus.ToFabricate,
        NextStatus = OrderStatus.InProgress,
        CustomerDisplayName = "Jean Dupont",
        CurrentStatusDisplay = "À fabriquer",
        NextStatusDisplay = "En fabrication",
    };

    // =========================================================================================================
    // Refus — commande historique incohérente.
    // =========================================================================================================

    [Fact]
    public async Task ArticleVerre_SurProduitMonture_EstRefuseSansAucunEffet()
    {
        var dbPath = PathFor("historique-incoherent.db");
        EnsureSchema(dbPath);
        var (orderId, productId) = SeedOrder(dbPath, ProductCategoryEnum.MONTURE, OrderItemType.LensOd, "MON-8001");

        using (var context = CreateContext(dbPath))
        {
            var act = () => CreateUseCase(context).ExecuteAsync(EntrerEnFabrication(orderId));

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
            thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage,
                "la fabrication oppose le MÊME message métier stable que la vente");
        }

        // Relecture depuis un contexte NEUF : le décrément passe par ExecuteUpdateAsync, invisible au tracker.
        using var verify = CreateContext(dbPath);

        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status
            .Should().Be(OrderStatus.ToFabricate, "le statut ne doit pas avancer");
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial, "aucun stock ne doit être touché");
        verify.StockMovements.Count().Should().Be(0, "aucun mouvement ne doit être créé");
        verify.WorkshopSheets.Count().Should().Be(0, "aucune fiche atelier ne doit être générée");
        verify.WorkshopSheetItems.Count().Should().Be(0);
        verify.Notifications.Count().Should().Be(0, "aucune notification d'entrée en fabrication");
    }

    [Fact]
    public async Task ArticleMonture_SurProduitVerre_EstRefuseSansAucunEffet()
    {
        var dbPath = PathFor("historique-incoherent-inverse.db");
        EnsureSchema(dbPath);
        var (orderId, productId) = SeedOrder(dbPath, ProductCategoryEnum.VERRE, OrderItemType.Frame, "VER-8002");

        using (var context = CreateContext(dbPath))
        {
            var act = () => CreateUseCase(context).ExecuteAsync(EntrerEnFabrication(orderId));

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
            thrown.Which.Message.Should().Be(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.ToFabricate);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity.Should().Be(StockInitial);
        verify.StockMovements.Count().Should().Be(0);
        verify.WorkshopSheets.Count().Should().Be(0);
        verify.Notifications.Count().Should().Be(0);
    }

    // =========================================================================================================
    // Non-régression — une commande cohérente continue de passer normalement.
    // =========================================================================================================

    [Fact]
    public async Task ArticleVerre_SurProduitVerre_ContinueDePasserNormalement()
    {
        var dbPath = PathFor("historique-coherent.db");
        EnsureSchema(dbPath);
        var (orderId, productId) = SeedOrder(dbPath, ProductCategoryEnum.VERRE, OrderItemType.LensOd, "VER-8100");

        using (var context = CreateContext(dbPath))
        {
            var result = await CreateUseCase(context).ExecuteAsync(EntrerEnFabrication(orderId));

            result.OrderFound.Should().BeTrue();
            result.NewStatus.Should().Be(OrderStatus.InProgress);
            result.CreatedStockMovementCount.Should().Be(1);
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.InProgress);
        verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity
            .Should().Be(StockInitial - 1, "un verre sort du stock exactement une fois, à la fabrication");
        verify.StockMovements.Count().Should().Be(1);
        verify.WorkshopSheets.Count().Should().Be(1, "la fiche atelier reste générée sur un flux cohérent");
    }

    /// <summary>
    /// Un article <b>sans produit</b> conserve son comportement historique : P3-11 n'invente aucune politique
    /// pour les articles dépourvus de clé étrangère produit. Il n'était décrémenté par personne, et ne l'est
    /// toujours pas — mais il ne bloque pas non plus la commande.
    /// </summary>
    [Fact]
    public async Task ArticleSansProduit_ConserveSonComportementHistorique()
    {
        var dbPath = PathFor("article-sans-produit.db");
        EnsureSchema(dbPath);

        long orderId;
        using (var context = CreateContext(dbPath))
        {
            var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
            context.Customers.Add(customer);
            context.SaveChanges();

            var sale = new Sale { SaleNumber = "VTE-000002", CustomerId = customer.CustomerId, FinalAmount = 100m };
            context.Sales.Add(sale);
            context.SaveChanges();

            var order = new Order
            {
                OrderNumber = "CMD-000002",
                SaleId = sale.SaleId,
                OrderDate = DateTime.UtcNow,
                Status = OrderStatus.ToFabricate,
                OrderItems =
                {
                    new OrderItem { ProductId = null, ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 80m }
                }
            };
            context.Orders.Add(order);
            context.SaveChanges();
            orderId = order.OrderId;
        }

        using (var context = CreateContext(dbPath))
        {
            var result = await CreateUseCase(context).ExecuteAsync(EntrerEnFabrication(orderId));
            result.OrderFound.Should().BeTrue();
            result.CreatedStockMovementCount.Should().Be(0, "aucun produit ⇒ aucun mouvement, comme avant P3-11");
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single(o => o.OrderId == orderId).Status.Should().Be(OrderStatus.InProgress);
        verify.StockMovements.Count().Should().Be(0);
    }
}
