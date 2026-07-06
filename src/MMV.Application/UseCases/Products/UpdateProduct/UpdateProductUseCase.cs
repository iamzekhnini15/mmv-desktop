using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.UpdateProduct;

/// <summary>
/// Implémentation du use case « Modifier un produit » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la branche édition de <c>ProductFormViewModel.ExecuteSaveAsync</c> (et sa méthode
/// <c>UpdateCategorySpecificDetailsAsync</c>) vers la couche Application.
/// </summary>
/// <remarks>
/// Recharge d'abord le produit avec ses détails (<c>GetByIdWithDetailsAsync</c>) pour éviter les conflits de
/// tracking et mettre à jour/créer les détails, exactement comme le flux d'origine. Mono-écriture (Update +
/// <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class UpdateProductUseCase : IUpdateProductUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductUseCase(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<UpdateProductResult> ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var product = await _productRepository.GetByIdWithDetailsAsync(command.ProductId, cancellationToken);
        if (product is null)
            return new UpdateProductResult { ProductFound = false, ProductId = command.ProductId };

        product.Reference = command.Reference;
        product.Name = command.Name;
        product.Description = command.Description;
        product.PurchasePrice = command.PurchasePrice;
        product.SalePrice = command.SalePrice;
        product.RecommendedPrice = command.RecommendedPrice;
        product.StockQuantity = command.StockQuantity;
        product.StockAlertThreshold = command.StockAlertThreshold;
        product.Category = command.Category;
        product.SupplierId = command.SupplierId ?? 0;

        ApplyCategorySpecificDetails(product, command);

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateProductResult { ProductFound = true, ProductId = product.ProductId };
    }

    /// <summary>
    /// Met à jour (ou crée si absents) les détails spécifiques à la catégorie uniquement si au moins un champ
    /// pertinent est renseigné. Port iso-fonctionnel de <c>UpdateCategorySpecificDetailsAsync</c>.
    /// </summary>
    private static void ApplyCategorySpecificDetails(Product product, UpdateProductCommand c)
    {
        switch (c.Category)
        {
            case ProductCategoryEnum.VERRE:
                if (!string.IsNullOrEmpty(c.GlassMaterial) || !string.IsNullOrEmpty(c.GlassType))
                {
                    product.GlassDetail ??= new GlassDetail { ProductId = product.ProductId };
                    product.GlassDetail.Material = Enum.TryParse<GlassMaterial>(c.GlassMaterial, out var glassMat) ? glassMat : null;
                    product.GlassDetail.GlassType = Enum.TryParse<GlassType>(c.GlassType, out var glassTyp) ? glassTyp : null;
                    product.GlassDetail.Diameter = c.GlassDiameter;
                    product.GlassDetail.Index = c.GlassIndex;
                    product.GlassDetail.PowerLimitMin = c.PowerLimitMin;
                    product.GlassDetail.PowerLimitMax = c.PowerLimitMax;
                }
                break;

            case ProductCategoryEnum.LENTILLE:
                if (!string.IsNullOrEmpty(c.LensBrand) || !string.IsNullOrEmpty(c.LensModel))
                {
                    product.LensDetail ??= new LensDetail { ProductId = product.ProductId };
                    product.LensDetail.Brand = c.LensBrand;
                    product.LensDetail.Model = c.LensModel;
                    product.LensDetail.Material = Enum.TryParse<LensMaterial>(c.LensMaterial, out var lensMat) ? lensMat : null;
                    product.LensDetail.LensType = Enum.TryParse<LensType>(c.LensType, out var lensTyp) ? lensTyp : null;
                    product.LensDetail.Diameter = c.LensDiameter;
                    product.LensDetail.BaseCurve = c.LensBaseCurve;
                    product.LensDetail.IsColored = c.LensIsColored;
                    product.LensDetail.Duration = Enum.TryParse<LensDuration>(c.LensDuration, out var lensDur) ? lensDur : null;
                }
                break;

            case ProductCategoryEnum.MONTURE:
            case ProductCategoryEnum.CLIPS:
            case ProductCategoryEnum.PLASTIC:
            case ProductCategoryEnum.SOLAIRE:
                if (!string.IsNullOrEmpty(c.AccessoryColor) || !string.IsNullOrEmpty(c.AccessorySize) || !string.IsNullOrEmpty(c.AccessoryMaterial))
                {
                    product.AccessoryDetail ??= new AccessoryDetail { ProductId = product.ProductId };
                    product.AccessoryDetail.Color = c.AccessoryColor;
                    product.AccessoryDetail.Size = c.AccessorySize;
                    product.AccessoryDetail.Material = c.AccessoryMaterial;
                }
                break;
        }
    }
}
