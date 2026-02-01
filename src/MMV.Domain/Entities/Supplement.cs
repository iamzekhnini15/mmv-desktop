namespace MMV.Domain.Entities;

/// <summary>
/// Représente un supplément optionnel pour les verres (anti-reflet, anti-blue, etc.).
/// </summary>
public class Supplement
{
    /// <summary>
    /// Identifiant unique du supplément.
    /// </summary>
    public long SupplementId { get; set; }

    /// <summary>
    /// Nom du supplément.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Prix supplémentaire à ajouter.
    /// </summary>
    public decimal SupplementPrice { get; set; }

    // Navigation Properties
    /// <summary>
    /// Relations avec les verres qui supportent ce supplément.
    /// </summary>
    public virtual ICollection<GlassSupplement> GlassSupplements { get; set; } = new List<GlassSupplement>();
}
