using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente un article dans une commande.
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Identifiant unique de l'article de commande.
    /// </summary>
    public long OrderItemId { get; set; }

    /// <summary>
    /// Identifiant de la commande parente.
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// Identifiant du produit associé (optionnel).
    /// </summary>
    public long? ProductId { get; set; }

    /// <summary>
    /// Type d'article (Monture, Verre OD, Verre OG, Accessoire).
    /// </summary>
    public OrderItemType ItemType { get; set; }

    /// <summary>
    /// Quantité commandée.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Prix unitaire de l'article (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal UnitPrice { get; set; }

    // ========== DONNÉES DE FABRICATION (nullable si Monture/Accessoire) ==========
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
    /// Commande parente à laquelle appartient cet article.
    /// </summary>
    public virtual Order Order { get; set; } = null!;

    /// <summary>
    /// Produit associé (si applicable).
    /// </summary>
    public virtual Product? Product { get; set; }
}
