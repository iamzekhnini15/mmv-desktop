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
    /// Quantité déplacée (positive pour entrée, négative pour sortie).
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
