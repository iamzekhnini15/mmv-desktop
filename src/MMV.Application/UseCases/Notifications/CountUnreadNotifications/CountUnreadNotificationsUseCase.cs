using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Notifications.CountUnreadNotifications;

/// <summary>
/// Implémentation du query use case « Compter les notifications non lues » (P2D-3). Déplace, sans changement de
/// comportement observable, la lecture <c>INotificationRepository.CountUnreadAsync</c> auparavant faite par
/// <c>NotificationsListViewModel.LoadNotificationsAsync</c>.
/// </summary>
public sealed class CountUnreadNotificationsUseCase : ICountUnreadNotificationsUseCase
{
    private readonly INotificationRepository _notificationRepository;

    public CountUnreadNotificationsUseCase(INotificationRepository notificationRepository)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(CountUnreadNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        return await _notificationRepository.CountUnreadAsync(cancellationToken);
    }
}
