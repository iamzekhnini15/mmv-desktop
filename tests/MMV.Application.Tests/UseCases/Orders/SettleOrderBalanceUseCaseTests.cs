using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Orders;

/// <summary>
/// P2B-2G — Cinquième vertical slice : use case « Encaisser le solde restant d'une commande »
/// (<see cref="SettleOrderBalanceUseCase"/>), extrait iso-fonctionnellement de
/// <c>OrderDetailViewModel.ExecuteEncashBalanceAsync</c>.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>encaisse le solde nominal : sur la vente liée, l'acompte devient le montant final et le restant 0,
///         et crée la notification (texte préservé au caractère près, format <c>:F2</c> compris) ;</item>
///   <item>n'émet aucune notification quand le repository de notifications est absent (<c>null</c>), tout en
///         mettant à jour le paiement ;</item>
///   <item>renvoie <c>OrderFound = false</c> sans écrire si la commande est introuvable ;</item>
///   <item>annule TOUTE l'écriture (rollback transactionnel) si la création de notification échoue : le paiement
///         de la vente reste inchangé (frontière transactionnelle dans le use case, R-23) ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories que le flux d'origine, partageant un unique <see cref="OpticDbContext"/> et
/// la frontière transactionnelle <see cref="EfTransactionRunner"/>.
/// </summary>
public sealed class SettleOrderBalanceUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public SettleOrderBalanceUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2g-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Construit le use case réel au-dessus d'un unique contexte (DbContext + transaction partagés).</summary>
    private static SettleOrderBalanceUseCase CreateUseCase(
        OpticDbContext context,
        INotificationRepository? notificationRepository)
    {
        return new SettleOrderBalanceUseCase(
            new OrderRepository(context),
            new SaleRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            notificationRepository);
    }

    private static SettleOrderBalanceUseCase CreateUseCase(OpticDbContext context, bool withNotifications = true)
        => CreateUseCase(context, withNotifications ? new NotificationRepository(context) : null);

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// Crée une vente parente (avec client) puis une commande liée. La vente parente est nécessaire car
    /// <c>GetWithItemsAsync</c> inclut la navigation requise <c>Order → Sale</c> (INNER JOIN). Le paiement de la
    /// vente est partiellement réglé (acompte &lt; montant final, solde restant &gt; 0), reproduisant l'état dans
    /// lequel le flux d'origine permet l'encaissement (<c>HasRemainingBalance</c>).
    /// </summary>
    private static long SeedOrder(
        string databasePath,
        string orderNumber,
        decimal finalAmount,
        decimal depositAmount,
        decimal remainingAmount)
    {
        using var context = CreateContext(databasePath);

        var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
        context.Customers.Add(customer);
        context.SaveChanges();

        var sale = new Sale
        {
            SaleNumber = "VTE-000001",
            CustomerId = customer.CustomerId,
            FinalAmount = finalAmount,
            DepositAmount = depositAmount,
            RemainingAmount = remainingAmount,
        };
        context.Sales.Add(sale);
        context.SaveChanges();

        var order = new Order
        {
            OrderNumber = orderNumber,
            SaleId = sale.SaleId,
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.Ready,
            OrderItems =
            {
                new OrderItem
                {
                    ItemType = OrderItemType.LensOd,
                    Quantity = 1,
                    UnitPrice = finalAmount,
                }
            }
        };
        context.Orders.Add(order);
        context.SaveChanges();
        return order.OrderId;
    }

    /// <summary>Faux repository de notifications qui échoue à la création (pour prouver le rollback).</summary>
    private sealed class ThrowingNotificationRepository : INotificationRepository
    {
        public Task<Notification> CreateAsync(Notification entity, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Échec création notification (test rollback).");

        public Task<Notification?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IList<Notification>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Notification> UpdateAsync(Notification entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Notification entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(long id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IEnumerable<Notification>> GetUnreadNotificationsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkAllAsReadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountUnreadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // ------------------------------------------------------------------
    // (1) Encaissement nominal : paiement vente mis à jour + notification
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NominalSettle_UpdatesSalePayment_AndCreatesNotification()
    {
        var dbPath = PathFor("nominal.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, "CMD-000100", finalAmount: 100m, depositAmount: 40m, remainingAmount: 60m);

        SettleOrderBalanceResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new SettleOrderBalanceCommand { OrderId = orderId });
        }

        result.OrderFound.Should().BeTrue();
        result.Order.Should().NotBeNull();
        result.AmountEncashed.Should().Be(60m, "le montant encaissé est le solde restant avant encaissement");
        result.OldRemainingAmount.Should().Be(60m);
        result.NewRemainingAmount.Should().Be(0m);
        result.IsFullyPaid.Should().BeTrue();
        result.HasNotification.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.DepositAmount.Should().Be(100m, "l'acompte devient le montant final");
        sale.RemainingAmount.Should().Be(0m, "le solde restant devient nul");

        var expectedMessage = $"Le solde de {60m:F2} € a été encaissé pour la commande CMD-000100.";
        var notification = verify.Notifications.AsNoTracking().Single();
        notification.Type.Should().Be("PaymentReceived");
        notification.Title.Should().Be("Paiement encaissé - CMD-000100");
        notification.Message.Should().Be(expectedMessage);
        notification.EntityId.Should().Be(orderId);
        notification.EntityType.Should().Be("Order");
        notification.IsRead.Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // (2) Sans repository de notifications : paiement mis à jour, aucune notification
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithoutNotificationRepository_UpdatesPayment_ButCreatesNoNotification()
    {
        var dbPath = PathFor("no-notif.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, "CMD-000200", finalAmount: 250m, depositAmount: 100m, remainingAmount: 150m);

        SettleOrderBalanceResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context, withNotifications: false);
            result = await useCase.ExecuteAsync(new SettleOrderBalanceCommand { OrderId = orderId });
        }

        result.OrderFound.Should().BeTrue();
        result.HasNotification.Should().BeFalse();
        result.AmountEncashed.Should().Be(150m);

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.DepositAmount.Should().Be(250m);
        sale.RemainingAmount.Should().Be(0m, "le paiement est encaissé même sans notification");
        verify.Notifications.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (3) Commande introuvable : OrderFound = false, aucune écriture
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OrderNotFound_ReturnsNotFound_AndWritesNothing()
    {
        var dbPath = PathFor("notfound.db");
        EnsureSchema(dbPath);

        SettleOrderBalanceResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new SettleOrderBalanceCommand { OrderId = 99999 });
        }

        result.OrderFound.Should().BeFalse();
        result.Order.Should().BeNull();

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Should().BeEmpty();
        verify.Sales.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (4) Échec de la notification → rollback complet (paiement inchangé)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenNotificationFails_RollsBack_PaymentUnchanged()
    {
        var dbPath = PathFor("rollback.db");
        EnsureSchema(dbPath);
        var orderId = SeedOrder(dbPath, "CMD-000300", finalAmount: 100m, depositAmount: 40m, remainingAmount: 60m);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context, new ThrowingNotificationRepository());

            Func<Task> act = () => useCase.ExecuteAsync(new SettleOrderBalanceCommand { OrderId = orderId });
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        using var verify = CreateContext(dbPath);
        var sale = verify.Sales.AsNoTracking().Single();
        sale.DepositAmount.Should().Be(40m, "rollback complet : l'acompte reste inchangé");
        sale.RemainingAmount.Should().Be(60m, "rollback complet : le solde restant reste inchangé");
        verify.Notifications.AsNoTracking().Should().BeEmpty("aucune notification persistée après rollback");
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
    // (6) Dépendances critiques manquantes → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutTransactionRunner_Throws()
    {
        var dbPath = PathFor("ctor-runner.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new SettleOrderBalanceUseCase(
            new OrderRepository(context),
            new SaleRepository(context),
            new UnitOfWork(context),
            transactionRunner: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithoutOrderRepository_Throws()
    {
        var dbPath = PathFor("ctor-repo.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action act = () => _ = new SettleOrderBalanceUseCase(
            orderRepository: null!,
            new SaleRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context));

        act.Should().Throw<ArgumentNullException>();
    }
}
