using MMV.Domain.Enums;

namespace MMV.Application.UseCases.WorkshopSheets;

/// <summary>
/// Ligne (lecture seule) d'une fiche atelier — P3-6B. Projection plate d'un
/// <c>WorkshopSheetItem</c> : aucune entité EF suivie ne franchit la frontière applicative.
/// </summary>
/// <remarks>
/// Toutes les valeurs proviennent du <b>snapshot</b> figé à la génération. Aucune donnée n'est relue depuis le
/// catalogue ni depuis le client : une fiche ancienne affiche ce qu'elle affichait le jour de sa création.
/// </remarks>
public sealed class WorkshopSheetItemDto
{
    /// <summary>Position figée de la ligne dans le bon d'atelier (à partir de 0).</summary>
    public int Position { get; init; }

    /// <summary>Type d'article (Monture, Verre OD, Verre OG, Accessoire).</summary>
    public OrderItemType ItemType { get; init; }

    /// <summary>Référence produit figée au snapshot (peut être <c>null</c> : ligne sans produit).</summary>
    public string? ProductReference { get; init; }

    /// <summary>Désignation produit figée au snapshot.</summary>
    public string? ProductName { get; init; }

    /// <summary>Catégorie produit figée au snapshot.</summary>
    public string? ProductCategory { get; init; }

    /// <summary>Quantité figée au snapshot.</summary>
    public int Quantity { get; init; }

    /// <summary>Type d'usage du verre.</summary>
    public LensUsageType? UsageType { get; init; }

    /// <summary>Sphère <b>source</b> (notation d'ordonnance).</summary>
    public double? SourceSphere { get; init; }

    /// <summary>Cylindre <b>source</b>.</summary>
    public double? SourceCylinder { get; init; }

    /// <summary>Axe <b>source</b>.</summary>
    public int? SourceAxis { get; init; }

    /// <summary>Addition (hors portée de la transposition).</summary>
    public double? Addition { get; init; }

    /// <summary>Valeur du prisme (hors portée de la transposition).</summary>
    public double? PrismValue { get; init; }

    /// <summary>Base du prisme (hors portée de la transposition).</summary>
    public PrismBase? PrismBase { get; init; }

    /// <summary>Acuité visuelle (hors portée de la transposition).</summary>
    public string? VisualAcuity { get; init; }

    /// <summary>Sphère <b>transposée</b> (notation atelier), ou <c>null</c> si non transposable.</summary>
    public double? TransposedSphere { get; init; }

    /// <summary>Cylindre <b>transposé</b>, ou <c>null</c> si non transposable.</summary>
    public double? TransposedCylinder { get; init; }

    /// <summary>Axe <b>transposé</b>, ou <c>null</c> si non transposable.</summary>
    public int? TransposedAxis { get; init; }

    /// <summary>Vrai si une notation atelier transposée significative a été produite.</summary>
    public bool HasTransposition { get; init; }
}
