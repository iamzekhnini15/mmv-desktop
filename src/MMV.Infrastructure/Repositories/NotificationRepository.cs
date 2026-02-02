using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des notifications.
/// </summary>
public class NotificationRepository : BaseRepository<Notification, long>, INotificationRepository
{
    public NotificationRepository(OpticDbContext context) : base(context) { }

    public async Task<IEnumerable<Notification>> GetUnreadNotificationsAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(n => !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        await _context.Notifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true),
                cancellationToken);
    }

    public async Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await GetByIdAsync(notificationId, cancellationToken);
        if (notification != null)
        {
            notification.IsRead = true;
            await UpdateAsync(notification, cancellationToken);
        }
    }

    public async Task<int> CountUnreadAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .CountAsync(n => !n.IsRead, cancellationToken);
    }
}
