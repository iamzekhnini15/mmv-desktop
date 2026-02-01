namespace MMV.Domain.Entities;

/// <summary>
/// Table d'association entre les verres et les suppléments disponibles.
/// </summary>
public class GlassSupplement
{
    /// <summary>
    /// Identifiant du verre.
    /// </summary>
    public long GlassId { get; set; }

    /// <summary>
    /// Identifiant du supplément.
    /// </summary>
    public long SupplementId { get; set; }

    // Navigation Properties
    /// <summary>
    /// Détails du verre.
    /// </summary>
    public virtual GlassDetail Glass { get; set; } = null!;

    /// <summary>
    /// Supplément associé.
    /// </summary>
    public virtual Supplement Supplement { get; set; } = null!;
}
