using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Ligne <b>snapshot</b> d'une <see cref="WorkshopSheet"/> (P3-6B) : une ligne par <see cref="OrderItem"/> réel
/// de la commande au moment de la génération (monture, verre OD, verre OG, accessoire…).
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune clé étrangère vers <see cref="Product"/>.</b> La désignation, la référence et la catégorie sont
/// <b>copiées par valeur</b> : la fiche survit intacte à un renommage, à une désactivation ou à une
/// réorganisation du catalogue. <see cref="SourceProductId"/> n'est conservé qu'à titre <i>informatif</i>
/// (traçabilité) et ne doit jamais être déréférencé pour construire l'affichage.
/// </para>
/// <para>
/// <b>Deux notations conservées côte à côte.</b> Les champs <c>Source*</c> portent la correction telle qu'elle
/// figure sur la commande (donc, in fine, sur l'ordonnance) ; les champs <c>Transposed*</c> portent la
/// <b>notation atelier</b> équivalente calculée par <c>OpticalTranspositionService</c>. La source n'est jamais
/// remplacée. <see cref="HasTransposition"/> indique si une notation transposée <b>significative</b> a
/// réellement pu être produite (cylindre non nul <i>et</i> axe présent).
/// </para>
/// <para>
/// <b>Hors portée de la transposition.</b> <see cref="Addition"/>, <see cref="PrismValue"/>,
/// <see cref="PrismBase"/> et <see cref="VisualAcuity"/> sont copiés <b>inchangés</b> : la transposition ne
/// touche que le trio sphère / cylindre / axe.
/// </para>
/// </remarks>
public class WorkshopSheetItem
{
    /// <summary>Identifiant unique de la ligne de fiche.</summary>
    public long WorkshopSheetItemId { get; set; }

    /// <summary>Identifiant de la fiche parente.</summary>
    public long WorkshopSheetId { get; set; }

    /// <summary>
    /// Position déterministe de la ligne dans le bon d'atelier (à partir de <c>0</c>), figée au snapshot afin que
    /// l'ordre d'affichage ne dépende jamais de l'ordre de restitution de la base.
    /// </summary>
    public int Position { get; set; }

    /// <summary>Type d'article (Monture, Verre OD, Verre OG, Accessoire).</summary>
    public OrderItemType ItemType { get; set; }

    /// <summary>
    /// Identifiant du produit d'origine, <b>informatif uniquement</b> (aucune clé étrangère, aucune relecture).
    /// </summary>
    public long? SourceProductId { get; set; }

    /// <summary>Référence produit figée au snapshot.</summary>
    public string? ProductReferenceSnapshot { get; set; }

    /// <summary>Désignation produit figée au snapshot.</summary>
    public string? ProductNameSnapshot { get; set; }

    /// <summary>Catégorie produit figée au snapshot (représentation textuelle stable).</summary>
    public string? ProductCategorySnapshot { get; set; }

    /// <summary>Quantité figée au snapshot.</summary>
    public int Quantity { get; set; }

    /// <summary>Type d'usage du verre (Loin, Près, Progressif), copié inchangé.</summary>
    public LensUsageType? UsageType { get; set; }

    /// <summary>Sphère <b>source</b> (telle que prescrite/commandée).</summary>
    public double? SourceSphere { get; set; }

    /// <summary>Cylindre <b>source</b>.</summary>
    public double? SourceCylinder { get; set; }

    /// <summary>Axe <b>source</b> (degrés).</summary>
    public int? SourceAxis { get; set; }

    /// <summary>Addition (presbytie), copiée inchangée — hors portée de la transposition.</summary>
    public double? Addition { get; set; }

    /// <summary>Valeur du prisme, copiée inchangée — hors portée de la transposition.</summary>
    public double? PrismValue { get; set; }

    /// <summary>Base du prisme, copiée inchangée — hors portée de la transposition.</summary>
    public PrismBase? PrismBase { get; set; }

    /// <summary>Acuité visuelle, copiée inchangée — hors portée de la transposition.</summary>
    public string? VisualAcuity { get; set; }

    /// <summary>Sphère <b>transposée</b> (notation atelier), ou <c>null</c> si non transposable.</summary>
    public double? TransposedSphere { get; set; }

    /// <summary>Cylindre <b>transposé</b> (notation atelier), ou <c>null</c> si non transposable.</summary>
    public double? TransposedCylinder { get; set; }

    /// <summary>Axe <b>transposé</b> (notation atelier), ou <c>null</c> si non transposable.</summary>
    public int? TransposedAxis { get; set; }

    /// <summary>
    /// Vrai si une notation transposée <b>significative</b> a été produite (cylindre non nul et axe présent).
    /// Faux quand il n'y a rien à transposer (pas de cylindre, ou cylindre nul) ou quand la transposition est
    /// <b>impossible</b> faute d'axe — auquel cas aucun axe n'est inventé.
    /// </summary>
    public bool HasTransposition { get; set; }

    // Navigation Properties
    /// <summary>Fiche parente.</summary>
    public virtual WorkshopSheet WorkshopSheet { get; set; } = null!;
}
