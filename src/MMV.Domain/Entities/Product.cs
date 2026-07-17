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

    private string _reference = string.Empty;

    /// <summary>
    /// Référence unique du produit (valeur d'affichage). La saisie est <b>nettoyée des espaces externes</b>
    /// (Trim) mais sa casse est conservée pour l'affichage. Toute écriture recalcule automatiquement
    /// <see cref="NormalizedReference"/>, garantissant que les deux ne divergent jamais — quel que soit le
    /// chemin d'écriture (use case, seed de test, matérialisation EF).
    /// </summary>
    public string Reference
    {
        get => _reference;
        set
        {
            _reference = (value ?? string.Empty).Trim();
            NormalizedReference = NormalizeReference(_reference);
        }
    }

    /// <summary>
    /// Représentation <b>normalisée</b> de la référence, servant de clé d'unicité (index unique en base) et de
    /// comparaison insensible à la casse et aux espaces externes. Jamais saisie directement : elle est dérivée de
    /// <see cref="Reference"/> par <see cref="NormalizeReference"/>. Le setter privé reste accessible à EF Core
    /// pour la matérialisation.
    /// </summary>
    public string NormalizedReference { get; private set; } = string.Empty;

    /// <summary>
    /// Fonction de normalisation <b>unique, partagée et déterministe</b> de la référence produit : suppression des
    /// espaces externes (Trim) puis passage en majuscules <b>invariantes de culture</b>
    /// (<see cref="string.ToUpperInvariant"/>). Ne dépend jamais de la culture courante de la machine, afin que
    /// deux postes produisent la même clé d'unicité. Deux références comme <c>"ABC-123"</c>, <c>"abc-123"</c>,
    /// <c>" ABC-123"</c> et <c>"ABC-123 "</c> convergent vers la même valeur normalisée.
    /// </summary>
    public static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().ToUpperInvariant();

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
