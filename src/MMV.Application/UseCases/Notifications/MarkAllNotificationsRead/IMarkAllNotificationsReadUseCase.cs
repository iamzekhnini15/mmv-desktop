using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;

/// <summary>
/// Cas d'utilisation « Marquer toutes les notifications comme lues » (P2C-GLOBAL). Remplace l'orchestration
/// auparavant portée par <c>NotificationsListViewModel.MarkAllAsReadAsync</c> (appel direct
/// <c>INotificationRepository.MarkAllAsReadAsync</c> + <c>IUnitOfWork.SaveChangesAsync</c>).
/// </summary>
public interface IMarkAllNotificationsReadUseCase
{
    /// <summary>Marque toutes les notifications comme lues et persiste le changement.</summary>
    Task<MarkAllNotificationsReadResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
