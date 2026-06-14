using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Stock.CreateStockMovement;

/// <summary>
/// Implémentation du use case « Créer un mouvement manuel de stock » (P2B-2F). Déplace, <b>sans changement de
/// comportement observable</b>, l'orchestration métier qui vivait dans la boucle par ligne de
/// <c>StockMovementFormViewModel.SaveAsync</c> vers la couche Application. Réutilise telles quelles les
/// primitives P2A (<see cref="ITransactionRunner"/>, <see cref="IStockMutationService"/>) et les repositories
/// existants.
/// </summary>
/// <remarks>
/// <para>
/// La frontière transactionnelle est désormais ouverte <b>ici</b> (et non plus dans la ViewModel) : pour une
/// même ligne, l'effet sur le stock (décrément sûr / incrément / ajustement) et la création du mouvement
/// participent à la même transaction (commit si succès, rollback complet si exception — aucune écriture
/// partielle). C'est strictement le comportement P2A-1D-R2 d'origine, déplacé.
/// </para>
/// <para>
/// Sémantique conservée à l'identique : pour une <see cref="StockMovementType.Out"/>, décrément atomique
/// conditionnel via <see cref="IStockMutationService.DecrementStockAsync"/> (jamais négatif ;
/// <see cref="MMV.Domain.Exceptions.InsufficientStockException"/> si stock insuffisant, aucune écriture). Pour une
/// <see cref="StockMovementType.In"/>, incrément direct ; pour un <see cref="StockMovementType.Adjustment"/>,
/// correction en valeur absolue. La quantité du mouvement reste positive (telle que saisie) ; le motif est repris
/// tel quel depuis la commande.
/// </para>
/// </remarks>
public sealed class CreateStockMovementUseCase : ICreateStockMovementUseCase
{
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly IStockMutationService _stockMutationService;

    public CreateStockMovementUseCase(
        IStockMovementRepository stockMovementRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        IStockMutationService stockMutationService)
    {
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Frontière transactionnelle obligatoire (P2A-1C, R-23) : décrément/incrément + mouvement = tout ou rien.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        // Décrément de stock sûr obligatoire (P2A-1D, R-09) : une sortie ne peut pas rendre le stock négatif.
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
    }

    /// <inheritdoc />
    public async Task<CreateStockMovementResult> ExecuteAsync(CreateStockMovementCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Recharger le produit frais pour éviter les conflits de tracking (iso-fonctionnel).
        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            // Produit introuvable : la ligne est comptée en erreur côté ViewModel, aucune écriture (comme l'ancien `continue`).
            return new CreateStockMovementResult { ProductFound = false };
        }

        var movement = new StockMovement
        {
            ProductId = product.ProductId,
            MovementType = command.MovementType,
            Quantity = command.Quantity,
            Reason = command.Reason,
        };

        // Frontière transactionnelle OBLIGATOIRE (P2A-1C, R-23) : ouverte ICI, dans le use case (plus jamais dans
        // la ViewModel). L'effet sur le stock et la création du mouvement sont persistés de façon atomique ; toute
        // exception annule TOUT (aucune écriture partielle).
        var newStockQuantity = await _transactionRunner.RunAsync(async ct =>
        {
            int resultingStock;
            switch (command.MovementType)
            {
                case StockMovementType.Out:
                    // Sortie standard : décrément atomique conditionnel (jamais négatif). En cas de stock
                    // insuffisant, InsufficientStockException est levée → le runner annule TOUTE l'écriture.
                    await _stockMutationService.DecrementStockAsync(product.ProductId, command.Quantity, ct);
                    resultingStock = product.StockQuantity - command.Quantity;
                    break;

                case StockMovementType.In:
                    // Entrée : incrément (aucun risque de négatif).
                    product.StockQuantity += command.Quantity;
                    await _productRepository.UpdateAsync(product, ct);
                    resultingStock = product.StockQuantity;
                    break;

                default: // StockMovementType.Adjustment
                    // Ajustement d'inventaire : correction CONTRÔLÉE en valeur absolue (quantité saisie ≥ 1).
                    product.StockQuantity = command.Quantity;
                    await _productRepository.UpdateAsync(product, ct);
                    resultingStock = product.StockQuantity;
                    break;
            }

            await _stockMovementRepository.CreateAsync(movement, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return resultingStock;
        }, cancellationToken);

        return new CreateStockMovementResult
        {
            ProductFound = true,
            StockMovementId = movement.MovementId,
            ProductId = product.ProductId,
            MovementType = command.MovementType,
            Quantity = command.Quantity,
            NewStockQuantity = newStockQuantity,
        };
    }
}
