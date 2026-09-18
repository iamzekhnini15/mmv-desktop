using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Notifications.CountUnreadNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P2D-3 — Query use cases du module Notifications (liste triée + compteur non lus). Vérifie sur un <b>vrai SQLite
/// temporaire</b> la projection vers DTO (aucune entité EF), le tri décroissant par date, le comptage, le cas vide
/// et les gardes.
/// </summary>
public sealed class NotificationQueryUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    public NotificationQueryUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d-notifq-" + Guid.NewGuid().ToString("N"));
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

    private static async Task SeedAsync(string dbPath)
    {
        using var context = CreateContext(dbPath);
        var repo = new NotificationRepository(context);
        await repo.CreateAsync(new Notification { Type = "LowStock", Title = "Ancien", Message = "m1", IsRead = true, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await repo.CreateAsync(new Notification { Type = "StockOut", Title = "Récent", Message = "m2", IsRead = false, CreatedAt = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) });
        await repo.CreateAsync(new Notification { Type = "LowStock", Title = "Milieu", Message = "m3", IsRead = false, CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc) });
        await new UnitOfWork(context).SaveChangesAsync();
    }

    [Fact]
    public async Task ListNotifications_ReturnsProjectedDtos_SortedByCreatedAtDescending()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);
        await SeedAsync(dbPath);

        IReadOnlyList<NotificationListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            result = await new ListNotificationsUseCase(new NotificationRepository(context)).ExecuteAsync(new ListNotificationsQuery());
        }

        result.Should().HaveCount(3);
        result.Should().AllBeOfType<NotificationListItemDto>();
        result.Select(n => n.Title).Should().ContainInOrder("Récent", "Milieu", "Ancien");
    }

    [Fact]
    public async Task CountUnread_ReturnsUnreadCount()
    {
        var dbPath = PathFor("count.db");
        EnsureSchema(dbPath);
        await SeedAsync(dbPath);

        using var context = CreateContext(dbPath);
        var count = await new CountUnreadNotificationsUseCase(new NotificationRepository(context))
            .ExecuteAsync(new CountUnreadNotificationsQuery());
        count.Should().Be(2);
    }

    [Fact]
    public async Task ListNotifications_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListNotificationsUseCase(new NotificationRepository(context)).ExecuteAsync(new ListNotificationsQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListNotificationsUseCase(new NotificationRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => new CountUnreadNotificationsUseCase(new NotificationRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_RejectNullRepository()
    {
        ((Action)(() => _ = new ListNotificationsUseCase(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new CountUnreadNotificationsUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
