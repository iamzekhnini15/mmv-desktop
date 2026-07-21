using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Products.CreateProduct;

/// <summary>
/// Implémentation du use case « Créer un produit ». En P3-4B, la validation métier (jusque-là portée par la seule
/// UI) est réellement appliquée <b>avant</b> toute écriture, et l'unicité de la référence est vérifiée sur sa
/// représentation normalisée.
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation (P3-1).</b> Le candidat est validé par le <see cref="ProductValidator"/> Domain (règle propriétaire
/// du Domain, aucune duplication) via <see cref="CommandValidation"/>. Commande invalide ⇒ aucune écriture.
/// </para>
/// <para>
/// <b>Unicité référence (P3-4B).</b> La référence est nettoyée et normalisée par l'entité
/// (<see cref="Product.NormalizeReference"/>). Une vérification applicative compacte (<c>AnyAsync</c>) fournit un
/// message métier clair en cas de doublon. Le filet de sécurité réel reste l'index unique en base sur
/// <see cref="Product.NormalizedReference"/> : si deux postes écrivent une référence équivalente quasi
/// simultanément, l'écriture perdante est rejetée par la base et <b>traduite</b> par
/// <c>PersistenceErrorMapper</c> (via <see cref="ITransactionRunner"/>) en <c>PersistenceException</c> neutre —
/// jamais un message SQLite/EF brut. C'est pourquoi l'écriture est enveloppée dans le runner transactionnel.
/// Ce cas concurrent (catégorie <c>UniqueConstraint</c>) est ensuite <b>converti dans le use case</b> en la
/// <b>même erreur métier stable</b> que la garde pré-écriture (<see cref="DuplicateReferenceMessage"/>) : le
/// chemin normal et le chemin concurrent produisent un résultat public identique ; aucune
/// <c>PersistenceException</c> n'échappe de ce cas. Les autres catégories de persistance restent propagées.
/// </para>
/// </remarks>
public sealed class CreateProductUseCase : ICreateProductUseCase
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'une référence est déjà utilisée par un autre produit. Exposé en
    /// constante pour que l'UI et les tests s'y réfèrent sans le dupliquer.
    /// </summary>
    public const string DuplicateReferenceMessage =
        "Un produit portant cette référence existe déjà.";

    /// <summary>
    /// Message métier stable renvoyé lorsqu'aucun fournisseur n'est fourni (P3-9). Le fournisseur est
    /// <b>structurellement obligatoire</b> : <c>Product.SupplierId</c> est non nullable et sa FK est
    /// <c>Restrict</c> + <c>IsRequired</c>.
    /// </summary>
    public const string SupplierRequiredMessage = "Le fournisseur est obligatoire.";

    /// <summary>
    /// Message métier stable renvoyé lorsque le fournisseur désigné n'existe pas (P3-9) — identifiant inconnu, ou
    /// fournisseur supprimé sur un autre poste depuis le chargement du sélecteur.
    /// </summary>
    public const string SupplierNotFoundMessage = "Le fournisseur sélectionné est introuvable.";

    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication). Stateless, partagé.
    private static readonly ProductValidator ProductValidator = new();

    private readonly IProductRepository _productRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;

    public CreateProductUseCase(
        IProductRepository productRepository,
        ISupplierRepository supplierRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<CreateProductResult> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // P3-9 : le fournisseur est obligatoire. L'ancien « command.SupplierId ?? 0 » écrivait un 0 qu'aucun
        // fournisseur ne porte : une donnée MANQUANTE devenait une violation d'intégrité (DbUpdateException
        // vérifiée empiriquement, audit §16) au lieu d'une erreur de saisie lisible. Le 0 n'est plus jamais écrit.
        if (command.SupplierId is not > 0)
            return new CreateProductResult
            {
                ValidationErrors = new List<ValidationError> { new(nameof(Product.SupplierId), SupplierRequiredMessage) }
            };

        var supplierId = command.SupplierId.Value;

        var product = new Product
        {
            Reference = command.Reference, // le setter nettoie (Trim) et calcule NormalizedReference
            Name = command.Name,
            Description = command.Description,
            PurchasePrice = command.PurchasePrice,
            SalePrice = command.SalePrice,
            RecommendedPrice = command.RecommendedPrice,
            StockQuantity = command.StockQuantity,
            StockAlertThreshold = command.StockAlertThreshold,
            Category = command.Category,
            SupplierId = supplierId,
        };

        // P3-1 : validation de commande AVANT toute écriture.
        var validationErrors = CommandValidation.Validate(ProductValidator, product);
        if (validationErrors.Count > 0)
            return new CreateProductResult { ValidationErrors = validationErrors };

        // P3-4B : unicité normalisée — message métier clair (le filet DB reste l'index unique).
        var duplicate = await _productRepository.ExistsByNormalizedReferenceAsync(product.NormalizedReference, 0, cancellationToken);
        if (duplicate)
            return new CreateProductResult
            {
                ValidationErrors = new List<ValidationError> { new(nameof(Product.Reference), DuplicateReferenceMessage) }
            };

        ApplyCategorySpecificDetails(product, command);

        try
        {
            // P3-9 : l'existence du fournisseur est vérifiée DANS la transaction qui écrit — lecture fraîche
            // (AnyAsync), jamais FindAsync ni une navigation déjà chargée. Renvoyer un drapeau plutôt que d'écrire
            // laisse la transaction se valider à vide : aucun produit persisté.
            var supplierMissing = await _transactionRunner.RunAsync(async ct =>
            {
                if (!await _supplierRepository.ExistsFreshAsync(supplierId, ct))
                    return true;

                await _productRepository.CreateAsync(product, ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return false;
            }, cancellationToken);

            if (supplierMissing)
                return new CreateProductResult
                {
                    ValidationErrors = new List<ValidationError> { new(nameof(Product.SupplierId), SupplierNotFoundMessage) }
                };
        }
        catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.ConstraintViolation)
        {
            // P3-9 — filet FK. Une vérification d'existence ne VERROUILLE pas le fournisseur jusqu'à l'écriture :
            // il peut disparaître entre le contrôle et le SaveChanges. La FK Restrict arbitre alors, et la
            // violation est traduite en la MÊME erreur métier stable que la garde pré-écriture.
            //
            // La confirmation ci-dessous est délibérée : PersistenceErrorMapper classe TOUTE contrainte SQLite
            // (FK, NOT NULL, CHECK) en ConstraintViolation. Sans vérifier que le fournisseur a réellement disparu,
            // une violation d'une autre contrainte serait étiquetée « fournisseur introuvable » — un message faux,
            // ce qui reproduirait à l'envers le défaut que P3-9 corrige. Non confirmée, l'exception remonte
            // inchangée (le « throw; » préserve la pile) : on ne masque jamais une panne réelle.
            if (await _supplierRepository.ExistsFreshAsync(supplierId, cancellationToken))
                throw;

            return new CreateProductResult
            {
                ValidationErrors = new List<ValidationError> { new(nameof(Product.SupplierId), SupplierNotFoundMessage) }
            };
        }
        catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.UniqueConstraint)
        {
            // Course concurrente : entre la garde applicative et l'écriture, un autre poste a persisté une
            // référence équivalente. L'index unique a rejeté l'écriture perdante ; le runner l'a traduite en
            // PersistenceException neutre. On renvoie EXACTEMENT le même résultat métier stable que la garde
            // pré-écriture (aucune exception exposée, aucun message provider brut). Seule la catégorie
            // UniqueConstraint est traitée comme doublon ; toute autre erreur de persistance reste propagée
            // (filtre when) pour ne pas masquer une panne réelle.
            return new CreateProductResult
            {
                ValidationErrors = new List<ValidationError> { new(nameof(Product.Reference), DuplicateReferenceMessage) }
            };
        }

        return new CreateProductResult { ProductId = product.ProductId };
    }

    /// <summary>
    /// Construit les détails spécifiques à la catégorie (verre / lentille / accessoire) uniquement si au moins un
    /// champ pertinent est renseigné. À la création, seule la catégorie courante peut porter un détail : aucune
    /// combinaison incompatible n'est possible.
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
