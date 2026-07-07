using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

/// <summary>
/// DTO applicatif (lecture seule) d'un produit affiché dans la fiche fournisseur (P2D-2). Porte exactement les
/// champs consommés par <c>SupplierDetailView</c> (référence, nom, catégorie, prix de vente).
/// </summary>
public sealed class SupplierProductItemDto
{
    /// <summary>Référence unique du produit.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Désignation du produit.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Catégorie du produit.</summary>
    public ProductCategoryEnum Category { get; init; }

    /// <summary>Prix de vente unitaire.</summary>
    public decimal SalePrice { get; init; }
}
