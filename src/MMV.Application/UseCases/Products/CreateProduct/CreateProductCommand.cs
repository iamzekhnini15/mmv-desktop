using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.CreateProduct;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreateProductUseCase"/> — « Créer un produit » (P2C-GLOBAL). Porte tous les
/// champs saisis dans <c>ProductFormViewModel</c> (mode création), y compris les champs spécifiques à la catégorie
/// (verre / lentille / accessoire), transmis en chaînes déjà telles que présentées à l'interface.
/// </summary>
/// <remarks>
/// Déplacement iso-fonctionnel de la branche création de <c>ProductFormViewModel.ExecuteSaveAsync</c> +
/// <c>CreateCategorySpecificDetailsAsync</c>. Les libellés d'énumération (matière/type de verre, matière/type/durée
/// de lentille) sont parsés par le use case exactement comme le faisait la ViewModel (<c>Enum.TryParse</c>).
/// </remarks>
public sealed class CreateProductCommand
{
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
