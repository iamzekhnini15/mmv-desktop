namespace MMV.Domain.Enums;

/// <summary>
/// Types de lentilles de contact selon la correction.
/// </summary>
public enum LensType
{
    /// <summary>
    /// Lentilles unifocales
    /// </summary>
    UNIFOCAL,

    /// <summary>
    /// Lentilles multifocales
    /// </summary>
    MULTIFOCAL,

    /// <summary>
    /// Lentilles toriques (pour astigmatisme)
    /// </summary>
    TORIQUE
}
