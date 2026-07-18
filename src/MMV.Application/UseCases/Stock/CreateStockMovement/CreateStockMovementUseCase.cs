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
/// La frontière transactionnelle est ouverte <b>ici</b> (et non plus dans la ViewModel) : pour une même ligne,
/// l'effet sur le stock (décrément sûr / incrément / ajustement) et la création du mouvement participent à la
/// même transaction (commit si succès, rollback complet si exception — aucune écriture partielle).
/// </para>
/// <para>
/// P3-5 : les <b>trois</b> mutations passent désormais par <see cref="IStockMutationService"/> (plus aucune
/// écriture directe de <c>StockQuantity</c> dans le use case). <see cref="StockMovementType.Out"/> ⇒ décrément
/// atomique conditionnel (jamais négatif ; <see cref="MMV.Domain.Exceptions.InsufficientStockException"/> si
/// insuffisant). <see cref="StockMovementType.In"/> ⇒ incrément atomique (aucune quantité perdue en concurrence).
/// <see cref="StockMovementType.Adjustment"/> ⇒ mise à jour concurrent-safe basée sur la valeur lue
/// (<see cref="MMV.Domain.Exceptions.StockConcurrencyConflictException"/> si un autre poste a modifié le stock
/// entre-temps ; aucun <i>last-write-wins</i>). Le mouvement enregistre le <b>delta signé</b> (In : +q ; Out : −q ;
/// Adjustment : nouvelle − ancienne). Le motif est repris tel quel depuis la commande.
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

        // Frontière transactionnelle OBLIGATOIRE (P2A-1C, R-23) : ouverte ICI, dans le use case (plus jamais dans
        // la ViewModel). L'effet sur le stock (toujours via IStockMutationService, P3-5) et la création du mouvement
        // sont persistés de façon atomique ; toute exception annule TOUT (aucune écriture partielle).
        var (newStockQuantity, movementId) = await _transactionRunner.RunAsync(async ct =>
        {
            int resultingStock;
            int signedQuantity; // delta signé enregistré dans le mouvement (convention P3-5).
            switch (command.MovementType)
            {
                case StockMovementType.Out:
                    // Sortie standard : décrément atomique conditionnel (jamais négatif). En cas de stock
                    // insuffisant, InsufficientStockException est levée → le runner annule TOUTE l'écriture.
                    await _stockMutationService.DecrementStockAsync(product.ProductId, command.Quantity, ct);
                    resultingStock = product.StockQuantity - command.Quantity;
                    signedQuantity = -command.Quantity; // sortie ⇒ delta négatif.
                    break;

                case StockMovementType.In:
                    // Entrée : incrément ATOMIQUE (deux entrées concurrentes ne perdent aucune quantité).
                    resultingStock = await _stockMutationService.IncrementStockAsync(product.ProductId, command.Quantity, ct);
                    signedQuantity = command.Quantity; // entrée ⇒ delta positif.
                    break;

                default: // StockMovementType.Adjustment
                    // Ajustement d'inventaire : correction en valeur absolue, CONCURRENT-SAFE (P3-5). Un autre poste
                    // ayant modifié le stock entre-temps ⇒ StockConcurrencyConflictException → rollback complet.
                    var adjustment = await _stockMutationService.AdjustStockToAsync(product.ProductId, command.Quantity, ct);
                    resultingStock = adjustment.NewQuantity;
                    signedQuantity = adjustment.Delta; // ajustement ⇒ delta réel (nouvelle − ancienne).
                    break;
            }

            var movement = new StockMovement
            {
                ProductId = product.ProductId,
                MovementType = command.MovementType,
                Quantity = signedQuantity,
                Reason = command.Reason,
            };
            await _stockMovementRepository.CreateAsync(movement, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return (resultingStock, movement.MovementId);
        }, cancellationToken);

        return new CreateStockMovementResult
        {
            ProductFound = true,
            StockMovementId = movementId,
            ProductId = product.ProductId,
            MovementType = command.MovementType,
            Quantity = command.Quantity,
            NewStockQuantity = newStockQuantity,
        };
    }
}
