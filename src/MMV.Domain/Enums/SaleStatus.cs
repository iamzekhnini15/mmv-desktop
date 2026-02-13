namespace MMV.Domain.Enums;

/// <summary>
/// Représente le statut d'une vente dans son cycle de vie.
/// </summary>
public enum SaleStatus
{
    /// <summary>
    /// Vente créée, en attente (panier non finalisé).
    /// </summary>
    Draft,

    /// <summary>
    /// Vente confirmée, en attente des verres du fournisseur.
    /// </summary>
    AwaitingLenses,

    /// <summary>
    /// Verres reçus, fabrication de la lunette en cours.
    /// </summary>
    InFabrication,

    /// <summary>
    /// Lunette prête, en attente de récupération par le client.
    /// </summary>
    Ready,

    /// <summary>
    /// Lunette livrée au client, vente terminée.
    /// </summary>
    Delivered,

    /// <summary>
    /// Vente annulée.
    /// </summary>
    Cancelled
}
