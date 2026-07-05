using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;

/// <summary>
/// Cas d'utilisation « Générer les notifications de stock bas » (P2C-GLOBAL). Remplace l'orchestration dupliquée
/// auparavant portée par <c>NotificationsListViewModel.GenerateLowStockNotificationsAsync</c> et
/// <c>MainWindowViewModel.LoadUnreadNotificationsCountAsync</c> (lecture des produits, création conditionnelle des
/// notifications, <c>SaveChangesAsync</c>, puis comptage des non lues).
/// </summary>
public interface IGenerateLowStockNotificationsUseCase
{
    /// <summary>
    /// Pour chaque produit sous son seuil d'alerte sans notification « stock bas » non lue, crée une notification,
    /// persiste l'ensemble et renvoie le nombre créé ainsi que le nombre de notifications non lues.
    /// </summary>
    Task<GenerateLowStockNotificationsResult> ExecuteAsync(CancellationToken cancellationToken = default);
}
