using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.GetOrderDetails;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2D-7B — Query use case « Charger la fiche détaillée d'une commande » (<see cref="GetOrderDetailsUseCase"/>).
/// Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection composite vers un
/// <see cref="OrderDetailsDto"/> (vente + client + articles + produit) sans exposer d'entité EF, le cas
/// « commande introuvable » (null), et les gardes (query nulle, repository nul).
/// </summary>
public sealed class GetOrderDetailsUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public GetOrderDetailsUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d7-orderdetails-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedOrder(string dbPath)
    {
        using var context = CreateContext(dbPath);

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont", Phone = "0102030405" };
        var product = new Product
        {
            Reference = "M-1", Name = "Monture Z", Category = ProductCategoryEnum.MONTURE,
            Supplier = new Supplier { Name = "F" }, SalePrice = 90m, PurchasePrice = 30m, StockQuantity = 4,
        };
        var sale = new Sale
        {
            SaleNumber = "S-1", Customer = customer, FinalAmount = 150m, DepositAmount = 50m,
            RemainingAmount = 100m, PaymentMethod = PaymentMethod.Card,
        };
        var order = new Order
        {
            OrderNumber = "CMD-000123", OrderDate = new DateTime(2024, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            EstimatedDelivery = new DateTime(2024, 6, 5, 0, 0, 0, DateTimeKind.Utc), Status = OrderStatus.ToFabricate,
            Notes = "Livraison rapide", Sale = sale,
        };
        order.OrderItems.Add(new OrderItem
        {
            Product = product, ItemType = OrderItemType.Frame, Quantity = 1, UnitPrice = 90m,
        });
        order.OrderItems.Add(new OrderItem
        {
            ItemType = OrderItemType.LensOd, Quantity = 1, UnitPrice = 60m,
            Sphere = -2.0, Cylinder = -0.5, Axis = 90, Addition = 1.5,
        });

        context.Orders.Add(order);
        context.SaveChanges();
        return order.OrderId;
    }

    [Fact]
    public async Task Execute_ProjectsCompositeOrderDetails()
    {
        var dbPath = PathFor("order.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath);

        OrderDetailsDto? result;
        using (var context = CreateContext(dbPath))
        {
            result = await new GetOrderDetailsUseCase(new OrderRepository(context))
                .ExecuteAsync(new GetOrderDetailsQuery { OrderId = orderId });
        }

        result.Should().NotBeNull();
        result!.Should().BeOfType<OrderDetailsDto>();
        result!.OrderNumber.Should().Be("CMD-000123");
        result.Status.Should().Be(OrderStatus.ToFabricate);
        result.EstimatedDelivery.Should().Be(new DateTime(2024, 6, 5, 0, 0, 0, DateTimeKind.Utc));
        result.Notes.Should().Be("Livraison rapide");

        result.Sale.Should().NotBeNull();
        result.Sale!.FinalAmount.Should().Be(150m);
        result.Sale.DepositAmount.Should().Be(50m);
        result.Sale.RemainingAmount.Should().Be(100m);
        result.Sale.PaymentMethod.Should().Be(PaymentMethod.Card);
        result.Sale.Customer.Should().NotBeNull();
        result.Sale.Customer!.FirstName.Should().Be("Jean");
        result.Sale.Customer.LastName.Should().Be("Dupont");
        result.Sale.Customer.Phone.Should().Be("0102030405");

        result.Items.Should().HaveCount(2);
        var frame = result.Items.Single(i => i.ItemType == OrderItemType.Frame);
        frame.Quantity.Should().Be(1);
        frame.UnitPrice.Should().Be(90m);
        frame.Product.Should().NotBeNull();
        frame.Product!.Name.Should().Be("Monture Z");
        frame.Product.Reference.Should().Be("M-1");

        var lens = result.Items.Single(i => i.ItemType == OrderItemType.LensOd);
        lens.Sphere.Should().Be(-2.0);
        lens.Cylinder.Should().Be(-0.5);
        lens.Axis.Should().Be(90);
        lens.Addition.Should().Be(1.5);
        lens.Product.Should().BeNull("l'article verre n'est pas rattaché à un produit catalogue");
    }

    [Fact]
    public async Task Execute_DoesNotReturnDomainEntities()
    {
        var dbPath = PathFor("no-entity.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath);

        using var context = CreateContext(dbPath);
        var result = await new GetOrderDetailsUseCase(new OrderRepository(context))
            .ExecuteAsync(new GetOrderDetailsQuery { OrderId = orderId });

        ((object)result!).Should().NotBeAssignableTo<Order>();
        result!.Items.Should().NotContain(i => (object)i is OrderItem);
    }

    [Fact]
    public async Task Execute_OrderNotFound_ReturnsNull()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);

        var result = await new GetOrderDetailsUseCase(new OrderRepository(context))
            .ExecuteAsync(new GetOrderDetailsQuery { OrderId = 99999 });

        result.Should().BeNull();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new GetOrderDetailsUseCase(new OrderRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new GetOrderDetailsUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
