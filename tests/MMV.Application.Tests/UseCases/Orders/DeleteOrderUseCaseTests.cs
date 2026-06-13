using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.DeleteOrder;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2B-2H — Sixième vertical slice : use case « Supprimer une commande »
/// (<see cref="DeleteOrderUseCase"/>), extrait iso-fonctionnellement de
/// <c>OrdersViewModel.OnDeleteOrderRequested</c>.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>supprime nominalement une commande existante et renvoie <c>OrderFound = true</c> ;</item>
///   <item>renvoie <c>OrderFound = false</c> sans écrire si la commande est introuvable ;</item>
///   <item>persiste effectivement la suppression (la commande n'est plus dans la base après exécution) ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>constructeur sans <see cref="IOrderRepository"/> : <see cref="ArgumentNullException"/> ;</item>
///   <item>constructeur sans <see cref="IUnitOfWork"/> : <see cref="ArgumentNullException"/>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine. Aucune transaction runner nécessaire
/// (mono-écriture : une seule opération <c>DeleteAsync</c> + un seul <c>SaveChangesAsync</c>).
/// </summary>
public sealed class DeleteOrderUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public DeleteOrderUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2h-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
                Directory.Delete(_workDirectory, recursive: true);
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static DeleteOrderUseCase CreateUseCase(OpticDbContext context)
        => new(new OrderRepository(context), new UnitOfWork(context));

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Crée une vente parente (avec client) puis une commande liée. La vente est nécessaire car
    /// <c>Order.SaleId</c> est une clé étrangère non nulle.
    /// </summary>
    private static long SeedOrder(string databasePath, string orderNumber)
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
            Status = OrderStatus.New,
        };
        context.Orders.Add(order);
        context.SaveChanges();
        return order.OrderId;
    }

    // ------------------------------------------------------------------
    // (1) Suppression nominale : commande trouvée et supprimée
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NominalDelete_ReturnsOrderFound_AndDeletesFromDatabase()
    {
        var dbPath = PathFor("nominal.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, "CMD-000001");

        DeleteOrderResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeleteOrderCommand { OrderId = orderId });
        }

        result.OrderFound.Should().BeTrue();
        result.OrderId.Should().Be(orderId);

        using var verify = CreateContext(dbPath);
        var remaining = await verify.Orders.AsNoTracking().CountAsync();
        remaining.Should().Be(0, "la commande doit être supprimée de la base");
    }

    // ------------------------------------------------------------------
    // (2) Commande introuvable : OrderFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ReturnsNotFound_AndWritesNothing()
    {
        var dbPath = PathFor("notfound.db");
        EnsureSchema(dbPath);

        DeleteOrderResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new DeleteOrderCommand { OrderId = 99999 });
        }

        result.OrderFound.Should().BeFalse();
        result.OrderId.Should().Be(99999);

        using var verify = CreateContext(dbPath);
        verify.Orders.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (3) Persistance effective : la commande n'existe plus après exécution
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AfterDeletion_OrderDoesNotExistInDatabase()
    {
        var dbPath = PathFor("persist.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, "CMD-000002");

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new DeleteOrderCommand { OrderId = orderId });
        }

        using var verify = CreateContext(dbPath);
        var order = await verify.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.OrderId == orderId);
        order.Should().BeNull("la commande supprimée ne doit pas être retrouvable");
    }

    // ------------------------------------------------------------------
    // (4) Commande nulle → ArgumentNullException
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
    // (5) Dépendances critiques manquantes → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutOrderRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteOrderUseCase(
            orderRepository: null!,
            new UnitOfWork(context));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutUnitOfWork_Throws()
    {
        var dbPath = PathFor("ctor-uow.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new DeleteOrderUseCase(
            new OrderRepository(context),
            unitOfWork: null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
