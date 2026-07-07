using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Notifications.CountUnreadNotifications;

/// <summary>
/// Query use case « Compter les notifications non lues » (P2D-3). Remplace
/// <c>INotificationRepository.CountUnreadAsync</c> côté UI.
/// </summary>
public interface ICountUnreadNotificationsUseCase
{
    /// <summary>Renvoie le nombre de notifications non lues.</summary>
    Task<int> ExecuteAsync(CountUnreadNotificationsQuery query, CancellationToken cancellationToken = default);
}
