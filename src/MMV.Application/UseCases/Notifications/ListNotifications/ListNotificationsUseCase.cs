using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Notifications.ListNotifications;

/// <summary>
/// Implémentation du query use case « Lister les notifications » (P2D-3). Déplace, sans changement de comportement
/// observable, la lecture de <c>NotificationsListViewModel.LoadNotificationsAsync</c>
/// (<c>INotificationRepository.GetAllAsync</c>), en appliquant le tri décroissant par date de création (comme à
/// l'affichage) et en projetant vers un <see cref="NotificationListItemDto"/> plat.
/// </summary>
public sealed class ListNotificationsUseCase : IListNotificationsUseCase
{
    private readonly INotificationRepository _notificationRepository;

    public ListNotificationsUseCase(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationListItemDto>> ExecuteAsync(ListNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var notifications = await _notificationRepository.GetAllAsync(cancellationToken);

        return notifications
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationListItemDto
            {
                NotificationId = n.NotificationId,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
            })
            .ToList();
    }
}
