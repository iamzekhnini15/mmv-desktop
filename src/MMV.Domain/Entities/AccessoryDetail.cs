namespace MMV.Domain.Entities;

/// <summary>
/// Détails spécifiques pour les montures et accessoires.
/// </summary>
public class AccessoryDetail
{
    /// <summary>
    /// Identifiant du produit (FK et PK).
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Couleur du produit.
    /// </summary>
    public string? Color { get; set; }

    /// <summary>
    /// Taille/calibre (ex: "52-18" pour calibre-pont).
    /// </summary>
    public string? Size { get; set; }

    /// <summary>
    /// Matière du produit.
    /// </summary>
    public string? Material { get; set; }

    // Navigation Properties
    /// <summary>
    /// Produit parent.
    /// </summary>
    public virtual Product Product { get; set; } = null!;
}
