using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des notifications.
/// </summary>
public interface INotificationRepository : IGenericRepository<Notification, long>
{
    /// <summary>
    /// Récupère toutes les notifications non lues.
    /// </summary>
    Task<IEnumerable<Notification>> GetUnreadNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marque toutes les notifications comme lues.
    /// </summary>
    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marque une notification comme lue.
    /// </summary>
    Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compte les notifications non lues.
    /// </summary>
    Task<int> CountUnreadAsync(CancellationToken cancellationToken = default);
}
