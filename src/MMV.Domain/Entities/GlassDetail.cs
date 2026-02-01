using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Détails spécifiques pour les verres correcteurs.
/// </summary>
public class GlassDetail
{
    /// <summary>
    /// Identifiant du produit (FK et PK).
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Matière du verre.
    /// </summary>
    public GlassMaterial? Material { get; set; }

    /// <summary>
    /// Type de verre (simple, double, multi focale).
    /// </summary>
    public GlassType? GlassType { get; set; }

    /// <summary>
    /// Diamètre du verre (ex: "65/70", "70/75").
    /// </summary>
    public string? Diameter { get; set; }

    /// <summary>
    /// Indice de réfraction (1.50 à 1.90).
    /// </summary>
    public decimal? Index { get; set; }

    /// <summary>
    /// Limite minimale de puissance supportée (ex: -6.00).
    /// </summary>
    public decimal? PowerLimitMin { get; set; }

    /// <summary>
    /// Limite maximale de puissance supportée (ex: +6.00).
    /// </summary>
    public decimal? PowerLimitMax { get; set; }

    // Navigation Properties
    /// <summary>
    /// Produit parent.
    /// </summary>
    public virtual Product Product { get; set; } = null!;

    /// <summary>
    /// Suppléments disponibles pour ce verre.
    /// </summary>
    public virtual ICollection<GlassSupplement> GlassSupplements { get; set; } = new List<GlassSupplement>();

    /// <summary>
    /// Grille tarifaire pour ce verre.
    /// </summary>
    public virtual ICollection<GlassPricingTier> PricingTiers { get; set; } = new List<GlassPricingTier>();
}
