namespace MMV.Application.UseCases.Orders.DeleteOrder;

/// <summary>
/// Commande d'entrée du use case « Supprimer une commande » (P2B-2H).
/// Contient uniquement les données nécessaires venant de la ViewModel : l'identifiant de la commande à supprimer.
/// </summary>
public sealed class DeleteOrderCommand
{
    /// <summary>Identifiant de la commande à supprimer.</summary>
    public long OrderId { get; init; }
}
