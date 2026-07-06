using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.CreateProduct;

/// <summary>
/// Implémentation du use case « Créer un produit » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la branche création de <c>ProductFormViewModel.ExecuteSaveAsync</c> (et sa méthode
/// <c>CreateCategorySpecificDetailsAsync</c>) vers la couche Application.
/// </summary>
/// <remarks>
/// <para>
/// La génération de notification « stock bas » présente dans la ViewModel d'origine n'est <b>pas</b> reportée :
/// dans l'application réelle, <c>ProductFormViewModel</c> était toujours construit sans
/// <c>INotificationRepository</c> (constructeur à 3 arguments), donc ce bloc ne s'exécutait jamais (code mort).
/// Les notifications de stock bas restent produites par le flux Notifications/Tableau de bord existant.
/// </para>
/// <para>Mono-écriture (Create + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.</para>
/// </remarks>
public sealed class CreateProductUseCase : ICreateProductUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateProductUseCase(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<CreateProductResult> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var product = new Product
        {
            Reference = command.Reference,
            Name = command.Name,
            Description = command.Description,
            PurchasePrice = command.PurchasePrice,
            SalePrice = command.SalePrice,
            RecommendedPrice = command.RecommendedPrice,
            StockQuantity = command.StockQuantity,
            StockAlertThreshold = command.StockAlertThreshold,
            Category = command.Category,
            SupplierId = command.SupplierId ?? 0,
        };

        ApplyCategorySpecificDetails(product, command);

        await _productRepository.CreateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateProductResult { ProductId = product.ProductId };
    }

    /// <summary>
    /// Construit les détails spécifiques à la catégorie (verre / lentille / accessoire) uniquement si au moins un
    /// champ pertinent est renseigné. Port iso-fonctionnel de <c>CreateCategorySpecificDetailsAsync</c>.
    /// </summary>
    private static void ApplyCategorySpecificDetails(Product product, CreateProductCommand c)
    {
        switch (c.Category)
        {
            case ProductCategoryEnum.VERRE:
                if (!string.IsNullOrEmpty(c.GlassMaterial) || !string.IsNullOrEmpty(c.GlassType))
                {
                    product.GlassDetail = new GlassDetail
                    {
                        Material = Enum.TryParse<GlassMaterial>(c.GlassMaterial, out var glassMat) ? glassMat : null,
                        GlassType = Enum.TryParse<GlassType>(c.GlassType, out var glassTyp) ? glassTyp : null,
                        Diameter = c.GlassDiameter,
                        Index = c.GlassIndex,
                        PowerLimitMin = c.PowerLimitMin,
                        PowerLimitMax = c.PowerLimitMax,
                    };
                }
                break;

            case ProductCategoryEnum.LENTILLE:
                if (!string.IsNullOrEmpty(c.LensBrand) || !string.IsNullOrEmpty(c.LensModel))
                {
                    product.LensDetail = new LensDetail
                    {
                        Brand = c.LensBrand,
                        Model = c.LensModel,
                        Material = Enum.TryParse<LensMaterial>(c.LensMaterial, out var lensMat) ? lensMat : null,
                        LensType = Enum.TryParse<LensType>(c.LensType, out var lensTyp) ? lensTyp : null,
                        Diameter = c.LensDiameter,
                        BaseCurve = c.LensBaseCurve,
                        IsColored = c.LensIsColored,
                        Duration = Enum.TryParse<LensDuration>(c.LensDuration, out var lensDur) ? lensDur : null,
                    };
                }
                break;

            case ProductCategoryEnum.MONTURE:
            case ProductCategoryEnum.CLIPS:
            case ProductCategoryEnum.PLASTIC:
            case ProductCategoryEnum.SOLAIRE:
                if (!string.IsNullOrEmpty(c.AccessoryColor) || !string.IsNullOrEmpty(c.AccessorySize) || !string.IsNullOrEmpty(c.AccessoryMaterial))
                {
                    product.AccessoryDetail = new AccessoryDetail
                    {
                        Color = c.AccessoryColor,
                        Size = c.AccessorySize,
                        Material = c.AccessoryMaterial,
                    };
                }
                break;
        }
    }
}
