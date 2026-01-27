namespace MMV.Domain.Entities;

/// <summary>
/// Représente une catégorie de produits.
/// </summary>
public class ProductCategory
{
    /// <summary>
    /// Identifiant unique de la catégorie.
    /// </summary>
    public long CategoryId { get; set; }

    /// <summary>
    /// Nom de la catégorie (unique).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description détaillée de la catégorie.
    /// </summary>
    public string? Description { get; set; }

    // Navigation Properties
    /// <summary>
    /// Produits appartenant à cette catégorie.
    /// </summary>
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
