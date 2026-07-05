using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P2C-GLOBAL — Use cases du module Notifications (marquage global + génération stock bas), extraits
/// iso-fonctionnellement de <c>NotificationsListViewModel</c> et <c>MainWindowViewModel</c>. Vérifie sur un
/// <b>vrai SQLite temporaire</b> le marquage comme lu, la création conditionnelle des alertes de stock bas et
/// l'anti-doublon (une seule alerte non lue par produit).
/// </summary>
public sealed class NotificationUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    public NotificationUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2cg-notif-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task MarkAllAsRead_MarksEveryNotification()
    {
        var dbPath = PathFor("markall.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var repo = new NotificationRepository(context);
            await repo.CreateAsync(new Notification { Type = "A", Title = "t1", Message = "m", IsRead = false });
            await repo.CreateAsync(new Notification { Type = "B", Title = "t2", Message = "m", IsRead = false });
            await new UnitOfWork(context).SaveChangesAsync();
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new MarkAllNotificationsReadUseCase(new NotificationRepository(context), new UnitOfWork(context));
            await useCase.ExecuteAsync();
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().All(n => n.IsRead).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateLowStock_CreatesOneAlertPerLowStockProduct_ThenIsIdempotent()
    {
        var dbPath = PathFor("lowstock.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var supplier = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "S" });
            await new UnitOfWork(context).SaveChangesAsync();
            var products = new ProductRepository(context);
            await products.CreateAsync(new Product { Reference = "LOW", Name = "Bas", Category = ProductCategoryEnum.MONTURE, SupplierId = supplier.SupplierId, StockQuantity = 1, StockAlertThreshold = 5 });
            await products.CreateAsync(new Product { Reference = "OK", Name = "Ok", Category = ProductCategoryEnum.MONTURE, SupplierId = supplier.SupplierId, StockQuantity = 20, StockAlertThreshold = 5 });
            await new UnitOfWork(context).SaveChangesAsync();
        }

        using (var context = CreateContext(dbPath))
        {
            var useCase = new GenerateLowStockNotificationsUseCase(new ProductRepository(context), new NotificationRepository(context), new UnitOfWork(context));
            var first = await useCase.ExecuteAsync();
            first.CreatedCount.Should().Be(1, "un seul produit est sous son seuil d'alerte");
            first.UnreadCount.Should().Be(1);
        }

        // Deuxième exécution : anti-doublon ⇒ aucune nouvelle notification.
        using (var context = CreateContext(dbPath))
        {
            var useCase = new GenerateLowStockNotificationsUseCase(new ProductRepository(context), new NotificationRepository(context), new UnitOfWork(context));
            var second = await useCase.ExecuteAsync();
            second.CreatedCount.Should().Be(0);
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.Type == "LowStock").Should().Be(1);
    }

    [Fact]
    public void Constructors_RejectNullDependencies()
    {
        ((Action)(() => _ = new MarkAllNotificationsReadUseCase(null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new GenerateLowStockNotificationsUseCase(null!, null!, null!))).Should().Throw<ArgumentNullException>();
    }
}
