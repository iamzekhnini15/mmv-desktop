namespace MMV.Application.UseCases.Stock.ListStockMovements;

/// <summary>
/// DTO applicatif (lecture seule) de la référence produit portée par une ligne de mouvement de stock (P2D-4).
/// Remplace la navigation EF <c>StockMovement.Product</c> pour préserver, sans exposer l'entité, les liaisons
/// d'affichage <c>Product.Name</c> / <c>Product.Reference</c> de <c>StockMovementsView</c>.
/// </summary>
public sealed class StockMovementProductRefDto
{
    /// <summary>Nom du produit concerné par le mouvement.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Référence (code) du produit concerné par le mouvement.</summary>
    public string? Reference { get; init; }
}
