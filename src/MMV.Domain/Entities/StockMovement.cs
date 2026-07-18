using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente un mouvement de stock (entrée, sortie, ajustement).
/// </summary>
public class StockMovement
{
    /// <summary>
    /// Identifiant unique du mouvement de stock.
    /// </summary>
    public long MovementId { get; set; }

    /// <summary>
    /// Identifiant du produit concerné.
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Type de mouvement (Entrée, Sortie, Ajustement).
    /// </summary>
    public StockMovementType MovementType { get; set; }

    /// <summary>
    /// Quantité déplacée, exprimée comme un <b>delta signé</b> selon une convention unique (P3-5) :
    /// <list type="bullet">
    ///   <item><see cref="StockMovementType.In"/> : valeur <b>positive</b> (+q) ;</item>
    ///   <item><see cref="StockMovementType.Out"/> : valeur <b>négative</b> (−q) ;</item>
    ///   <item><see cref="StockMovementType.Adjustment"/> : delta réel = <c>nouvelle quantité − ancienne quantité</c>
    ///         (positif, négatif, ou zéro).</item>
    /// </list>
    /// Ainsi la somme des <see cref="Quantity"/> d'un produit reconstitue directement la variation de stock, sans
    /// avoir à réinterpréter le signe via <see cref="MovementType"/>.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Raison du mouvement (ex: "Réception commande fournisseur", "Vente", "Inventaire").
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Identifiant de l'utilisateur ayant effectué le mouvement (optionnel).
    /// </summary>
    public long? PerformedByUserId { get; set; }

    /// <summary>
    /// Date et heure du mouvement.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    /// <summary>
    /// Produit concerné par ce mouvement.
    /// </summary>
    public virtual Product Product { get; set; } = null!;

    /// <summary>
    /// Utilisateur ayant effectué le mouvement.
    /// </summary>
    public virtual User? PerformedByUser { get; set; }
}
