namespace MMV.Domain.Enums;

/// <summary>
/// Représente le type d'article dans une commande.
/// </summary>
public enum OrderItemType
{
    /// <summary>
    /// Monture de lunettes.
    /// </summary>
    Frame,

    /// <summary>
    /// Verre pour l'œil droit.
    /// </summary>
    LensOd,

    /// <summary>
    /// Verre pour l'œil gauche.
    /// </summary>
    LensOg,

    /// <summary>
    /// Accessoire (étui, chiffon, etc.).
    /// </summary>
    Accessory
}
