using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.GetSaleFormReferenceData;

/// <summary>
/// DTO applicatif (lecture seule) d'un produit du catalogue pour le formulaire de vente (P2D-7A). Remplace
/// l'entité EF <c>Product</c> côté UI dans <c>SaleFormViewModel</c> (catalogue, verres suggérés, montures
/// compatibles). Porte les champs scalaires consommés par l'écran, plus <see cref="GlassDetail"/> (aplatissement
/// de la navigation EF <c>Product.GlassDetail</c>) nécessaire au filtrage de compatibilité optique.
/// </summary>
public sealed class SaleProductPickerItemDto
{
    /// <summary>Identifiant du produit.</summary>
    public long ProductId { get; init; }

    /// <summary>Référence du produit.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Désignation commerciale du produit.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Description détaillée (recherche textuelle).</summary>
    public string? Description { get; init; }

    /// <summary>Catégorie du produit.</summary>
    public ProductCategoryEnum Category { get; init; }

    /// <summary>Prix de vente unitaire.</summary>
    public decimal SalePrice { get; init; }

    /// <summary>Quantité en stock actuelle.</summary>
    public int StockQuantity { get; init; }

    /// <summary>Indique si le produit est actif dans le catalogue (filtre conservé côté présentation).</summary>
    public bool IsActive { get; init; }

    /// <summary>Détails verre (uniquement si <see cref="Category"/> = VERRE), sinon <c>null</c>.</summary>
    public SaleGlassDetailDto? GlassDetail { get; init; }
}
