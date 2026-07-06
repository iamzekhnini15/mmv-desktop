namespace MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;

/// <summary>
/// Sortie (DTO) du use case <see cref="GenerateLowStockNotificationsUseCase"/>.
/// </summary>
public sealed class GenerateLowStockNotificationsResult
{
    /// <summary>Nombre de notifications « stock bas » effectivement créées lors de cet appel.</summary>
    public int CreatedCount { get; init; }

    /// <summary>Nombre total de notifications non lues après génération (pour le badge d'interface).</summary>
    public int UnreadCount { get; init; }
}
