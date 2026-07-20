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

    /// <summary>
    /// Nombre d'alertes de stock bas <b>résolues</b> lors de cet appel (P3-8) : produit réapprovisionné, désactivé,
    /// supprimé, ou seuil abaissé sous le stock.
    ///
    /// <para>
    /// Ajout purement additif : aucune ViewModel ne le consomme, donc aucune UI n'est modifiée. Il rend la moitié
    /// « fermeture » de la réconciliation observable et testable au même titre que <see cref="CreatedCount"/>.
    /// </para>
    /// </summary>
    public int ResolvedCount { get; init; }
}
