using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.ListOrders;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2D-6 — Query use case « Lister les commandes » (<see cref="ListOrdersUseCase"/>). Vérifie sur un
/// <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers des <see cref="OrderListItemDto"/> composites
/// plats (jamais l'entité EF <c>Order</c>), le portage des montants et du client de la vente rattachée, le tri
/// décroissant par date de commande (comme les ViewModels d'origine), le cas vide et les gardes.
/// </summary>
public sealed class ListOrdersUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListOrdersUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d6-orders-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Crée un client, une vente parente (FK non nulle de la commande) et une commande liée.</summary>
    private static void SeedOrder(
        OpticDbContext context, string orderNumber, DateTime orderDate, OrderStatus status,
        string customerFirst, string customerLast, decimal finalAmount, decimal? deposit, decimal? remaining)
    {
        var customer = new Customer { FirstName = customerFirst, LastName = customerLast };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale
        {
            SaleNumber = "VTE-" + orderNumber,
            CustomerId = customer.CustomerId,
            FinalAmount = finalAmount,
            DepositAmount = deposit,
            RemainingAmount = remaining,
        };
        context.Sales.Add(sale);
        context.SaveChanges();

        context.Orders.Add(new Order
        {
            OrderNumber = orderNumber,
            SaleId = sale.SaleId,
            OrderDate = orderDate,
            EstimatedDelivery = orderDate.AddDays(14),
            Status = status,
            Notes = "Note " + orderNumber,
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task ListOrders_ReturnsProjectedDtos_SortedByOrderDateDescending_WithSaleAndCustomer()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            SeedOrder(context, "CMD-000001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                OrderStatus.New, "Alice", "Ancienne", 100m, 40m, 60m);
            SeedOrder(context, "CMD-000002", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                OrderStatus.InProgress, "Bob", "Recent", 250m, null, null);
        }

        IReadOnlyList<OrderListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListOrdersUseCase(new OrderRepository(context));
            result = await useCase.ExecuteAsync(new ListOrdersQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<OrderListItemDto>();
        // Tri décroissant par date : la plus récente d'abord (comme OrdersListViewModel / OrderKanbanViewModel).
        result[0].OrderNumber.Should().Be("CMD-000002");
        result[1].OrderNumber.Should().Be("CMD-000001");

        var recent = result[0];
        recent.Status.Should().Be(OrderStatus.InProgress);
        recent.Sale.Should().NotBeNull();
        recent.Sale!.FinalAmount.Should().Be(250m);
        recent.Sale.DepositAmount.Should().BeNull();
        recent.Sale.RemainingAmount.Should().BeNull();
        recent.Sale.Customer.Should().NotBeNull();
        recent.Sale.Customer!.FirstName.Should().Be("Bob");
        recent.Sale.Customer.LastName.Should().Be("Recent");

        var older = result[1];
        older.Sale!.FinalAmount.Should().Be(100m);
        older.Sale.DepositAmount.Should().Be(40m);
        older.Sale.RemainingAmount.Should().Be(60m);
        older.Notes.Should().Be("Note CMD-000001");
    }

    [Fact]
    public async Task ListOrders_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListOrdersUseCase(new OrderRepository(context)).ExecuteAsync(new ListOrdersQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListOrdersUseCase(new OrderRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListOrdersUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
