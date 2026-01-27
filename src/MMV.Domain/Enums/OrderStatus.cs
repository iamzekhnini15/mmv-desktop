namespace MMV.Domain.Enums;

/// <summary>
/// Représente le statut d'une commande dans le workflow de l'atelier.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Commande nouvellement créée.
    /// </summary>
    New,

    /// <summary>
    /// À fabriquer - en attente de fabrication.
    /// </summary>
    ToFabricate,

    /// <summary>
    /// En cours de fabrication.
    /// </summary>
    InProgress,

    /// <summary>
    /// Contrôle qualité en cours.
    /// </summary>
    QualityCheck,

    /// <summary>
    /// Prête pour retrait/livraison.
    /// </summary>
    Ready,

    /// <summary>
    /// Livrée au client.
    /// </summary>
    Delivered
}
