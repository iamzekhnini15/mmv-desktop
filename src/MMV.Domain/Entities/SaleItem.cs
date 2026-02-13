using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente un article dans une vente.
/// </summary>
public class SaleItem
{
    /// <summary>
    /// Identifiant unique de l'article de vente.
    /// </summary>
    public long SaleItemId { get; set; }

    /// <summary>
    /// Identifiant de la vente parente.
    /// </summary>
    public long SaleId { get; set; }

    /// <summary>
    /// Identifiant du produit vendu (optionnel).
    /// </summary>
    public long? ProductId { get; set; }

    /// <summary>
    /// Type d'article (Monture, Verre OD, Verre OG, Accessoire).
    /// </summary>
    public OrderItemType ItemType { get; set; } = OrderItemType.Frame;

    /// <summary>
    /// Quantité vendue.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Prix unitaire de l'article (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Prix total pour cette ligne (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal TotalPrice { get; set; }

    // ========== DONNÉES DE PRESCRIPTION (nullable si Monture/Accessoire) ==========
    /// <summary>
    /// Type d'usage du verre (Distance, Près, Progressif).
    /// </summary>
    public LensUsageType? UsageType { get; set; }

    /// <summary>
    /// Sphère de correction (dioptrie).
    /// </summary>
    public double? Sphere { get; set; }

    /// <summary>
    /// Cylindre de correction (astigmatisme).
    /// </summary>
    public double? Cylinder { get; set; }

    /// <summary>
    /// Axe de correction (0-180 degrés).
    /// </summary>
    public int? Axis { get; set; }

    /// <summary>
    /// Addition (presbytie).
    /// </summary>
    public double? Addition { get; set; }

    /// <summary>
    /// Valeur du prisme (dioptries prismatiques).
    /// </summary>
    public double? PrismValue { get; set; }

    /// <summary>
    /// Base du prisme (orientation).
    /// </summary>
    public PrismBase? PrismBase { get; set; }

    /// <summary>
    /// Acuité visuelle (format: 10/10, 8/10, etc.).
    /// </summary>
    public string? VisualAcuity { get; set; }

    // Navigation Properties
    /// <summary>
    /// Vente parente à laquelle appartient cet article.
    /// </summary>
    public virtual Sale Sale { get; set; } = null!;

    /// <summary>
    /// Produit vendu.
    /// </summary>
    public virtual Product? Product { get; set; }
}

