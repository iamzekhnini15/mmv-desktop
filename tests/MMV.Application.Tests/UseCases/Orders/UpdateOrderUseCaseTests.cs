using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2B-2I — Septième vertical slice : use case « Modifier une commande existante »
/// (<see cref="UpdateOrderUseCase"/>), extrait iso-fonctionnellement de la branche édition de
/// <c>OrderFormViewModel.SaveAsync</c> (méthode <c>UpdateExistingOrderAsync</c>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>met à jour les champs de la commande existante (date estimée, notes), reconstruit les lignes
///         (anciennes supprimées, nouvelles persistées) et conserve les paramètres optiques des verres ;</item>
///   <item>ignore les paramètres optiques pour les articles non-verres (monture) — règle du flux d'origine ;</item>
///   <item>vide les notes blanches vers <c>null</c> ;</item>
///   <item>préserve le statut de la commande (l'édition n'y touche pas) ;</item>
///   <item>commande introuvable : renvoie <c>OrderFound = false</c> sans écrire ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class UpdateOrderUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public UpdateOrderUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2i-" + Guid.NewGuid().ToString("N"));
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

    // NB — Enforcement des clés étrangères DÉSACTIVÉ ("Foreign Keys=False"), comme pour CreateOrderUseCaseTests :
    // le flux d'origine crée/édite une commande autonome avec Order.SaleId = 0 (aucune vente parente). Ce
    // comportement préexistant et orthogonal est inchangé par P2B-2I. Il s'agit toujours de vrai SQLite (fichier
    // + schéma réels), jamais du provider InMemory.
    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False;Foreign Keys=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Construit le use case réel au-dessus d'un unique contexte (DbContext partagé).</summary>
    private static UpdateOrderUseCase CreateUseCase(OpticDbContext context)
    {
        return new UpdateOrderUseCase(new OrderRepository(context), new UnitOfWork(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedProduct(string databasePath, ProductCategoryEnum category, string reference)
    {
        using var context = CreateContext(databasePath);
        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = reference,
            Name = "Produit Test",
            Category = category,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = 5,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    /// <summary>
    /// Sème une commande existante (statut <c>InProgress</c>) avec ses lignes initiales, rattachée à une vente
    /// parente réelle. La vente est nécessaire car <c>GetWithItemsAsync</c> charge la commande via la navigation
    /// <b>requise</b> <c>Order → Sale</c> (jointure interne) ; en production toute commande possède sa vente.
    /// </summary>
    private static long SeedExistingOrder(string databasePath, Action<Order> configure)
    {
        using var context = CreateContext(databasePath);
        var sale = new Sale { SaleNumber = "VTE-000100" };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            OrderNumber = "CMD-000100",
            EstimatedDelivery = new DateTime(2026, 1, 1),
            Notes = "Notes initiales",
            Status = OrderStatus.InProgress,
            SaleId = sale.SaleId,
        };
        configure(order);
        context.Orders.Add(order);
        context.SaveChanges();
        return order.OrderId;
    }

    // ------------------------------------------------------------------
    // (1) Édition nominale : champs mis à jour, lignes reconstruites, optique verre conservée, statut préservé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_UpdatesFields_RebuildsLines_PreservesLensOptics_AndStatus()
    {
        var dbPath = PathFor("update.db");
        EnsureSchema(dbPath);
        var oldFrameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-OLD");
        var lensId = SeedProduct(dbPath, ProductCategoryEnum.VERRE, "VER-NEW");

        // Commande initiale : 2 lignes (une monture + un verre) qui devront être entièrement remplacées.
        var orderId = SeedExistingOrder(dbPath, o =>
        {
            o.OrderItems.Add(new OrderItem { ProductId = oldFrameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 30m });
            o.OrderItems.Add(new OrderItem { ProductId = oldFrameId, ItemType = OrderItemType.Accessory, Quantity = 2, UnitPrice = 5m });
        });

        var newEstimated = new DateTime(2026, 3, 15);
        UpdateOrderResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new UpdateOrderCommand
            {
                OrderId = orderId,
                OrderNumber = "CMD-000100",
                EstimatedDelivery = newEstimated,
                Notes = "Notes modifiées",
                Lines = new[]
                {
                    new UpdateOrderLineCommand
                    {
                        ProductId = lensId,
                        ItemType = OrderItemType.LensOd,
                        Quantity = 1,
                        UnitPrice = 80m,
                        Sphere = -2.0,
                        Cylinder = -0.5,
                        Axis = 90,
                        Addition = 1.5,
                    }
                }
            };

            result = await useCase.ExecuteAsync(command);
        }

        result.OrderFound.Should().BeTrue();
        result.OrderId.Should().Be(orderId);
        result.OrderNumber.Should().Be("CMD-000100");
        result.Status.Should().Be(OrderStatus.InProgress, "l'édition ne touche pas le statut");
        result.EstimatedDelivery.Should().Be(newEstimated);

        using var verify = CreateContext(dbPath);
        var order = verify.Orders.AsNoTracking().Include(o => o.OrderItems).Single();
        order.EstimatedDelivery.Should().Be(newEstimated);
        order.Notes.Should().Be("Notes modifiées");
        order.Status.Should().Be(OrderStatus.InProgress);
        order.OrderItems.Should().ContainSingle("les anciennes lignes sont supprimées et remplacées");
        var item = order.OrderItems.Single();
        item.ProductId.Should().Be(lensId);
        item.ItemType.Should().Be(OrderItemType.LensOd);
        item.Quantity.Should().Be(1);
        item.UnitPrice.Should().Be(80m);
        item.Sphere.Should().Be(-2.0, "les paramètres optiques sont conservés pour les verres");
        item.Cylinder.Should().Be(-0.5);
        item.Axis.Should().Be(90);
        item.Addition.Should().Be(1.5);
    }

    // ------------------------------------------------------------------
    // (2) Article non-verre (monture) : les paramètres optiques sont ignorés
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_FrameLine_IgnoresOpticalParameters()
    {
        var dbPath = PathFor("frame.db");
        EnsureSchema(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-001");
        var orderId = SeedExistingOrder(dbPath, o =>
            o.OrderItems.Add(new OrderItem { ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 30m }));

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new UpdateOrderCommand
            {
                OrderId = orderId,
                OrderNumber = "CMD-000100",
                Lines = new[]
                {
                    new UpdateOrderLineCommand
                    {
                        ProductId = frameId,
                        ItemType = OrderItemType.Frame,
                        Quantity = 2,
                        UnitPrice = 35m,
                        // Valeurs optiques fournies mais qui doivent être ignorées pour une monture.
                        Sphere = -1.0,
                        Cylinder = -1.0,
                        Axis = 45,
                        Addition = 2.0,
                    }
                }
            };

            await useCase.ExecuteAsync(command);
        }

        using var verify = CreateContext(dbPath);
        var item = verify.Orders.AsNoTracking().Include(o => o.OrderItems).Single().OrderItems.Single();
        item.ItemType.Should().Be(OrderItemType.Frame);
        item.Sphere.Should().BeNull("les paramètres optiques ne sont conservés que pour les verres");
        item.Cylinder.Should().BeNull();
        item.Axis.Should().BeNull();
        item.Addition.Should().BeNull();
    }

    // ------------------------------------------------------------------
    // (3) Notes blanches → null
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BlankNotes_StoredAsNull()
    {
        var dbPath = PathFor("notes.db");
        EnsureSchema(dbPath);
        var frameId = SeedProduct(dbPath, ProductCategoryEnum.MONTURE, "MON-002");
        var orderId = SeedExistingOrder(dbPath, o =>
            o.OrderItems.Add(new OrderItem { ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 30m }));

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            var command = new UpdateOrderCommand
            {
                OrderId = orderId,
                OrderNumber = "CMD-000100",
                Notes = "   ",
                Lines = new[]
                {
                    new UpdateOrderLineCommand
                    {
                        ProductId = frameId, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 30m
                    }
                }
            };

            await useCase.ExecuteAsync(command);
        }

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Single().Notes
            .Should().BeNull("les notes blanches sont normalisées en null, comme le flux d'origine");
    }

    // ------------------------------------------------------------------
    // (4) Commande introuvable → OrderFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ReturnsNotFound_AndDoesNotWrite()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);

        UpdateOrderResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new UpdateOrderCommand { OrderId = 4242 });
        }

        result.OrderFound.Should().BeFalse();
        result.OrderId.Should().Be(4242);

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Should().BeEmpty("aucune commande n'est créée quand l'id est introuvable");
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

        Action act = () => _ = new UpdateOrderUseCase(orderRepository: null!, new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }
}
