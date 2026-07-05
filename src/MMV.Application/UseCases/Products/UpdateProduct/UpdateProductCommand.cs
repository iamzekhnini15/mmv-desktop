using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.UpdateProduct;

/// <summary>
/// Entrée (DTO) du use case <see cref="UpdateProductUseCase"/> — « Modifier un produit » (P2C-GLOBAL). Porte
/// l'identifiant et tous les champs (base + détails de catégorie) modifiés dans <c>ProductFormViewModel</c>.
/// </summary>
/// <remarks>
/// Déplacement iso-fonctionnel de la branche édition de <c>ProductFormViewModel.ExecuteSaveAsync</c> +
/// <c>UpdateCategorySpecificDetailsAsync</c>.
/// </remarks>
public sealed class UpdateProductCommand
{
    public long ProductId { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public decimal PurchasePrice { get; init; }
    public decimal SalePrice { get; init; }
    public decimal? RecommendedPrice { get; init; }
    public int StockQuantity { get; init; }
    public int StockAlertThreshold { get; init; }
    public ProductCategoryEnum Category { get; init; }
    public long? SupplierId { get; init; }

    // Détails « verre » (catégorie VERRE).
    public string? GlassMaterial { get; init; }
    public string? GlassType { get; init; }
    public string? GlassDiameter { get; init; }
    public decimal? GlassIndex { get; init; }
    public decimal? PowerLimitMin { get; init; }
    public decimal? PowerLimitMax { get; init; }

    // Détails « lentille » (catégorie LENTILLE).
    public string? LensBrand { get; init; }
    public string? LensModel { get; init; }
    public string? LensMaterial { get; init; }
    public string? LensType { get; init; }
    public decimal? LensDiameter { get; init; }
    public decimal? LensBaseCurve { get; init; }
    public bool LensIsColored { get; init; }
    public string? LensDuration { get; init; }

    // Détails « accessoire » (catégories MONTURE / CLIPS / PLASTIC / SOLAIRE).
    public string? AccessoryColor { get; init; }
    public string? AccessorySize { get; init; }
    public string? AccessoryMaterial { get; init; }
}
