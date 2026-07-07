using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Référence produit affichée sur un article de commande détaillée (P2D-7B) : <c>Product.Name</c> /
/// <c>Product.Reference</c> (fiche + fiche de fabrication) et <c>Product.Category</c> (fiche de fabrication,
/// monture).
/// </summary>
public sealed class OrderDetailsProductDto
{
    /// <summary>Désignation commerciale du produit.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Référence du produit.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Catégorie du produit.</summary>
    public ProductCategoryEnum Category { get; init; }
}
