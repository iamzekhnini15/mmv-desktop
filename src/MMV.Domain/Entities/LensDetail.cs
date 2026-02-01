using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Détails spécifiques pour les lentilles de contact.
/// </summary>
public class LensDetail
{
    /// <summary>
    /// Identifiant du produit (FK et PK).
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Marque de la lentille.
    /// </summary>
    public string? Brand { get; set; }

    /// <summary>
    /// Modèle de la lentille.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Matière de la lentille.
    /// </summary>
    public LensMaterial? Material { get; set; }

    /// <summary>
    /// Type de lentille (unifocal, multifocal, torique).
    /// </summary>
    public LensType? LensType { get; set; }

    /// <summary>
    /// Diamètre de la lentille (ex: 14.0, 14.5).
    /// </summary>
    public decimal? Diameter { get; set; }

    /// <summary>
    /// Courbure de la lentille (ex: 8.4, 8.6).
    /// </summary>
    public decimal? BaseCurve { get; set; }

    /// <summary>
    /// Indique si la lentille est colorée.
    /// </summary>
    public bool IsColored { get; set; } = false;

    /// <summary>
    /// Durée d'utilisation de la lentille.
    /// </summary>
    public LensDuration? Duration { get; set; }

    // Navigation Properties
    /// <summary>
    /// Produit parent.
    /// </summary>
    public virtual Product Product { get; set; } = null!;
}
