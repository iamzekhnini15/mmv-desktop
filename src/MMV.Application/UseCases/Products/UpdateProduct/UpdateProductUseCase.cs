using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Products.UpdateProduct;

/// <summary>
/// Implémentation du use case « Modifier un produit ». En P3-4B : validation métier réellement appliquée, unicité
/// normalisée de la référence (en ignorant le produit courant) et <b>cohérence catégorie / détails spécialisés</b>
/// garantie côté backend (les détails devenus incompatibles sont supprimés).
/// </summary>
/// <remarks>
/// Recharge le produit avec ses détails (<c>GetByIdWithDetailsAsync</c>) pour piloter la création/mise à jour/
/// suppression des détails. L'écriture est enveloppée dans <see cref="ITransactionRunner"/> afin que toute
/// violation d'unicité concurrente sur <see cref="Product.NormalizedReference"/> soit traduite en
/// <c>PersistenceException</c> neutre (jamais un message SQLite/EF brut), via le mécanisme existant
/// <c>PersistenceErrorMapper</c>. Ce cas concurrent (catégorie <c>UniqueConstraint</c>) est ensuite
/// <b>converti dans le use case</b> en la <b>même erreur métier stable</b> que la garde pré-écriture
/// (<c>CreateProductUseCase.DuplicateReferenceMessage</c>) : chemin normal et chemin concurrent produisent un
/// résultat public identique ; aucune <c>PersistenceException</c> n'échappe de ce cas. Les autres catégories de
/// persistance restent propagées.
/// </remarks>
public sealed class UpdateProductUseCase : IUpdateProductUseCase
{
    private static readonly ProductValidator ProductValidator = new();

    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;

    public UpdateProductUseCase(IProductRepository productRepository, IUnitOfWork unitOfWork, ITransactionRunner transactionRunner)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<UpdateProductResult> ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var product = await _productRepository.GetByIdWithDetailsAsync(command.ProductId, cancellationToken);
        if (product is null)
            return new UpdateProductResult { ProductFound = false, ProductId = command.ProductId };

        product.Reference = command.Reference; // le setter nettoie (Trim) et recalcule NormalizedReference
        product.Name = command.Name;
        product.Description = command.Description;
        product.PurchasePrice = command.PurchasePrice;
        product.SalePrice = command.SalePrice;
        product.RecommendedPrice = command.RecommendedPrice;
        // P3-5 : l'édition catalogue NE réécrit PLUS StockQuantity. Le stock est la propriété exclusive des use cases
        // de mouvement (décrément sûr, incrément atomique, ajustement concurrent-safe) ; réécrire ici la quantité
        // chargée dans le formulaire écraserait tout décrément concurrent survenu entre-temps (lost update).
        // command.StockQuantity est conservé dans la commande pour la compatibilité des appelants, mais non persisté.
        product.StockAlertThreshold = command.StockAlertThreshold;
        product.Category = command.Category;
        product.SupplierId = command.SupplierId ?? 0;

        // P3-1 : validation de commande AVANT toute écriture.
        var validationErrors = CommandValidation.Validate(ProductValidator, product);
        if (validationErrors.Count > 0)
            return new UpdateProductResult { ProductFound = true, ProductId = product.ProductId, ValidationErrors = validationErrors };

        // P3-4B : unicité normalisée en excluant le produit courant (il conserve sa propre référence).
        var duplicate = await _productRepository.ExistsByNormalizedReferenceAsync(product.NormalizedReference, product.ProductId, cancellationToken);
        if (duplicate)
            return new UpdateProductResult
            {
                ProductFound = true,
                ProductId = product.ProductId,
                ValidationErrors = new List<ValidationError> { new(nameof(Product.Reference), CreateProductUseCase.DuplicateReferenceMessage) }
            };

        ApplyCategorySpecificDetails(product, command);

        try
        {
            await _transactionRunner.RunAsync(async ct =>
            {
                // P3-5 : mise à jour catalogue qui EXCLUT StockQuantity de l'UPDATE (le stock ne bouge que par mouvement).
                await _productRepository.UpdateCatalogAsync(product, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.UniqueConstraint)
        {
            // Course concurrente : entre la garde applicative et l'écriture, un autre poste a persisté une
            // référence équivalente. L'index unique a rejeté l'écriture perdante ; le runner l'a traduite en
            // PersistenceException neutre. On renvoie EXACTEMENT le même résultat métier stable que la garde
            // pré-écriture (aucune exception exposée, aucun message provider brut). Seule la catégorie
            // UniqueConstraint est traitée comme doublon ; toute autre erreur de persistance reste propagée.
            return new UpdateProductResult
            {
                ProductFound = true,
                ProductId = product.ProductId,
                ValidationErrors = new List<ValidationError> { new(nameof(Product.Reference), CreateProductUseCase.DuplicateReferenceMessage) }
            };
        }

        return new UpdateProductResult { ProductFound = true, ProductId = product.ProductId };
    }

    /// <summary>
    /// Met à jour (ou crée) le détail compatible avec la catégorie courante et <b>supprime les détails devenus
    /// incompatibles</b> (P3-4B : cohérence garantie par le backend, pas seulement par l'UI). Le détail compatible
    /// n'est ni exigé ni supprimé quand ses champs sont vides : les commandes actuelles ne permettent pas de
    /// distinguer « champ jamais renseigné » de « champ volontairement vidé » (chaînes vides des deux côtés), donc
    /// le comportement legacy — conserver un détail existant partiellement rempli — est préservé.
    /// </summary>
    private static void ApplyCategorySpecificDetails(Product product, UpdateProductCommand c)
    {
        switch (c.Category)
        {
            case ProductCategoryEnum.VERRE:
                product.LensDetail = null;
                product.AccessoryDetail = null;
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
                product.GlassDetail = null;
                product.AccessoryDetail = null;
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
                product.GlassDetail = null;
                product.LensDetail = null;
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
