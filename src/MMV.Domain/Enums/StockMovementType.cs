namespace MMV.Domain.Enums;

/// <summary>
/// Représente le type de mouvement de stock.
/// </summary>
public enum StockMovementType
{
    /// <summary>
    /// Entrée de stock (réception, retour).
    /// </summary>
    In,

    /// <summary>
    /// Sortie de stock (vente, utilisation).
    /// </summary>
    Out,

    /// <summary>
    /// Ajustement manuel du stock (inventaire, correction).
    /// </summary>
    Adjustment
}
