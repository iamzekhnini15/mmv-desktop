using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente un produit du catalogue (monture, verre, accessoire).
/// Table mère contenant les champs communs à toutes les catégories.
/// </summary>
public class Product
{
    /// <summary>
    /// Identifiant unique du produit.
    /// </summary>
    public long ProductId { get; set; }

    /// <summary>
    /// Référence unique du produit.
    /// </summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// Désignation/Nom commercial du produit.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description détaillée du produit.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Catégorie du produit (ENUM).
    /// </summary>
    public ProductCategoryEnum Category { get; set; }

    /// <summary>
    /// Identifiant de la catégorie du produit (ancienne structure, conservée pour compatibilité).
    /// </summary>
    public long? CategoryId { get; set; }

    /// <summary>
    /// Identifiant du fournisseur (OBLIGATOIRE).
    /// </summary>
    public long SupplierId { get; set; }

    /// <summary>
    /// Prix d'achat unitaire (OBLIGATOIRE: type decimal pour argent).
    /// Pour les verres, c'est le prix de base auquel s'ajoutent les suppléments.
    /// </summary>
    public decimal PurchasePrice { get; set; }

    /// <summary>
    /// Prix de vente unitaire (OBLIGATOIRE: type decimal pour argent).
    /// Pour les verres, c'est le prix de base auquel s'ajoutent les suppléments.
    /// </summary>
    public decimal SalePrice { get; set; }

    /// <summary>
    /// Prix de vente conseillé.
    /// </summary>
    public decimal? RecommendedPrice { get; set; }

    /// <summary>
    /// Quantité en stock actuelle.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Seuil d'alerte de stock bas.
    /// </summary>
    public int StockAlertThreshold { get; set; } = 5;

    /// <summary>
    /// Spécifications techniques (stockées en JSON).
    /// </summary>
    public string? TechnicalSpecs { get; set; }

    /// <summary>
    /// Indique si le produit est actif dans le catalogue.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Date d'entrée du produit dans le catalogue.
    /// </summary>
    public DateTime EntryDate { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    /// <summary>
    /// Catégorie à laquelle appartient ce produit (ancienne structure, conservée pour compatibilité).
    /// </summary>
    public virtual ProductCategory? ProductCategory { get; set; }

    /// <summary>
    /// Fournisseur de ce produit (OBLIGATOIRE).
    /// </summary>
    public virtual Supplier Supplier { get; set; } = null!;

    /// <summary>
    /// Détails spécifiques si c'est un verre.
    /// </summary>
    public virtual GlassDetail? GlassDetail { get; set; }

    /// <summary>
    /// Détails spécifiques si c'est une lentille.
    /// </summary>
    public virtual LensDetail? LensDetail { get; set; }

    /// <summary>
    /// Détails spécifiques si c'est un accessoire/monture.
    /// </summary>
    public virtual AccessoryDetail? AccessoryDetail { get; set; }

    /// <summary>
    /// Articles de commande utilisant ce produit.
    /// </summary>
    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    /// <summary>
    /// Articles de vente de ce produit.
    /// </summary>
    public virtual ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();

    /// <summary>
    /// Historique des mouvements de stock.
    /// </summary>
    public virtual ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
