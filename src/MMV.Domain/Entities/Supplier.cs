namespace MMV.Domain.Entities;

/// <summary>
/// Représente un fournisseur de produits.
/// </summary>
public class Supplier
{
    /// <summary>
    /// Identifiant unique du fournisseur.
    /// </summary>
    public long SupplierId { get; set; }

    /// <summary>
    /// Nom du fournisseur.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Adresse email de contact.
    /// </summary>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// Numéro de téléphone.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Adresse postale complète.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Code de référence interne du fournisseur.
    /// </summary>
    public string? ReferenceCode { get; set; }

    // Navigation Properties
    /// <summary>
    /// Produits fournis par ce fournisseur.
    /// </summary>
    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
