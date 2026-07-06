using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Notifications.ListNotifications;

/// <summary>
/// Query use case « Lister les notifications » (P2D-3). Remplace <c>INotificationRepository.GetAllAsync</c> côté UI.
/// </summary>
public interface IListNotificationsUseCase
{
    /// <summary>
    /// Renvoie toutes les notifications, triées par date de création décroissante, projetées en
    /// <see cref="NotificationListItemDto"/> (jamais des entités EF suivies).
    /// </summary>
    Task<IReadOnlyList<NotificationListItemDto>> ExecuteAsync(ListNotificationsQuery query, CancellationToken cancellationToken = default);
}
