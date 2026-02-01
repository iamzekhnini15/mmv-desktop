namespace MMV.Domain.Entities;

/// <summary>
/// Grille tarifaire pour les verres selon la puissance de correction.
/// Permet de définir des prix différents selon la plage de puissance.
/// </summary>
public class GlassPricingTier
{
    /// <summary>
    /// Identifiant unique de l'entrée de la grille.
    /// </summary>
    public long TierId { get; set; }

    /// <summary>
    /// Identifiant du verre concerné.
    /// </summary>
    public long GlassId { get; set; }

    /// <summary>
    /// Puissance minimale de la plage (ex: 0.00).
    /// </summary>
    public decimal PowerMin { get; set; }

    /// <summary>
    /// Puissance maximale de la plage (ex: 2.00).
    /// </summary>
    public decimal PowerMax { get; set; }

    /// <summary>
    /// Prix d'achat pour cette plage de puissance.
    /// </summary>
    public decimal PurchasePriceGrid { get; set; }

    /// <summary>
    /// Prix de vente pour cette plage de puissance.
    /// </summary>
    public decimal SalePriceGrid { get; set; }

    // Navigation Properties
    /// <summary>
    /// Détails du verre parent.
    /// </summary>
    public virtual GlassDetail Glass { get; set; } = null!;
}
