using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.Orders.AdvanceOrderStatus;

/// <summary>
/// Use case « Faire avancer le statut / réception d'une commande ». En P3-5, le flux de <b>fabrication</b>
/// (<c>ToFabricate → InProgress</c>) devient <b>sûr et idempotent</b> en multi-poste : décrément atomique via
/// <see cref="IStockMutationService"/> (jamais négatif), prise de statut atomique conditionnelle
/// (<see cref="IOrderRepository.TryTransitionStatusAsync"/>) et frontière transactionnelle unique
/// (<see cref="ITransactionRunner"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Transaction.</b> Tout le flux (prise atomique du statut, décréments, mouvements, notification, sauvegarde)
/// s'exécute dans un unique <see cref="ITransactionRunner"/>. Une erreur de stock
/// (<see cref="InsufficientStockException"/>) ou un conflit de statut (<see cref="OrderStatusConflictException"/>)
/// annule <b>tout</b> : statut non avancé, aucun décrément, aucun mouvement, aucune notification.
/// </para>
/// <para>
/// <b>Idempotence du décrément (garde P3-5, pas la matrice P3-6).</b> Le statut est pris par un
/// <c>UPDATE … WHERE Status = statut attendu</c> (aucune comparaison en mémoire) : deux postes tentant simultanément
/// <c>ToFabricate → InProgress</c>, ou une répétition de la même transition, ne produisent qu'<b>un seul</b> ensemble
/// de décréments et de mouvements ; la seconde tentative est refusée par <see cref="OrderStatusConflictException"/>.
/// </para>
/// <para>
/// <b>Légalité de la transition (matrice P3-6).</b> Avant d'ouvrir la transaction, le couple
/// <c>CurrentStatus → NextStatus</c> est validé par <c>OrderStatusPolicy.IsAllowed</c> (source de vérité unique du
/// Domain). Un couple interdit (saut d'étape, retour arrière, même statut, sortie de
/// <see cref="OrderStatus.Delivered"/>) lève <see cref="InvalidOrderStatusTransitionException"/> <b>sans</b> aucune
/// écriture. Légalité (matrice) et conflit de concurrence (prise atomique) sont deux gardes complémentaires.
/// </para>
/// <para>
/// <see cref="INotificationRepository"/> reste <b>optionnel</b> (peut être <c>null</c>), reproduisant la garde
/// <c>if (_notificationRepository != null)</c> du flux d'origine.
/// </para>
/// </remarks>
public sealed class AdvanceOrderStatusUseCase : IAdvanceOrderStatusUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly IStockMutationService _stockMutationService;
    private readonly INotificationRepository? _notificationRepository;

    public AdvanceOrderStatusUseCase(
        IOrderRepository orderRepository,
        IStockMovementRepository stockMovementRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        IStockMutationService stockMutationService,
        INotificationRepository? notificationRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Frontière transactionnelle obligatoire (P2A-1C, R-23) : statut + décréments + mouvements = tout ou rien.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        // Décrément de stock sûr obligatoire (P2A-1D, R-09) : la fabrication ne peut plus rendre le stock négatif.
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
        // Notification optionnelle (comme le flux d'origine) : si null, aucune notification n'est créée.
        _notificationRepository = notificationRepository;
    }

    /// <inheritdoc />
    public async Task<AdvanceOrderStatusResult> ExecuteAsync(AdvanceOrderStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var previousStatus = command.CurrentStatus;
        var nextStatus = command.NextStatus;

        // Garde de LÉGALITÉ métier (P3-6) : la matrice unique du Domain valide le couple AVANT toute transaction.
        // Un couple interdit (saut d'étape, retour arrière, même statut, sortie de Delivered) est refusé ici même :
        // aucune transaction n'est ouverte ⇒ aucune prise de statut, aucun décrément, aucun mouvement, aucune
        // notification. La prise atomique conditionnelle (P3-5) reste ensuite responsable du CONFLIT de concurrence.
        if (!OrderStatusPolicy.IsAllowed(previousStatus, nextStatus))
        {
            throw new InvalidOrderStatusTransitionException(previousStatus, nextStatus);
        }

        return await _transactionRunner.RunAsync(async token =>
        {
            // Recharger la commande fraîche (articles + produits) pour construire les mouvements de fabrication.
            var fresh = await _orderRepository.GetWithItemsAsync(command.OrderId, token);
            if (fresh is null)
            {
                // « Commande introuvable. » côté ViewModel : aucun changement d'état, aucune écriture.
                return new AdvanceOrderStatusResult { OrderFound = false };
            }

            // Prise ATOMIQUE du statut : ne réussit que si le statut stocké est encore celui attendu. Empêche le
            // double décrément (concurrence / répétition) sans implémenter la matrice de transitions (P3-6).
            var transitionTaken = await _orderRepository.TryTransitionStatusAsync(command.OrderId, previousStatus, nextStatus, token);
            if (!transitionTaken)
            {
                throw new OrderStatusConflictException(command.OrderId, previousStatus);
            }

            // Sortie de stock lors du passage en fabrication (À fabriquer → En fabrication), désormais via le
            // décrément atomique sûr (jamais négatif). Stock insuffisant ⇒ InsufficientStockException ⇒ rollback.
            var stockMovementCount = 0;
            if (previousStatus == OrderStatus.ToFabricate && nextStatus == OrderStatus.InProgress)
            {
                stockMovementCount = await CreateStockMovementsForFabricationAsync(fresh, token);
            }

            // Créer une notification (si le repository est disponible), texte préservé au caractère près.
            var hasNotification = false;
            if (_notificationRepository != null)
            {
                var notification = new Notification
                {
                    Type = "OrderStatusChanged",
                    Title = $"Commande {fresh.OrderNumber} : {command.NextStatusDisplay}",
                    Message = $"La commande {fresh.OrderNumber} ({command.CustomerDisplayName}) est passée de " +
                              $"'{command.CurrentStatusDisplay}' à " +
                              $"'{command.NextStatusDisplay}'.",
                    EntityId = fresh.OrderId,
                    EntityType = "Order",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };
                await _notificationRepository.CreateAsync(notification, token);
                hasNotification = true;
            }

            // Écriture des mouvements + notification. Le statut a déjà été pris atomiquement ci-dessus ; le décrément
            // du stock est appliqué directement en base par le service. Tout est dans la même transaction (runner).
            await _unitOfWork.SaveChangesAsync(token);

            // Refléter le nouveau statut dans l'entité renvoyée à la ViewModel (post-sauvegarde : non re-persisté).
            fresh.Status = nextStatus;

            return new AdvanceOrderStatusResult
            {
                OrderFound = true,
                Order = fresh,
                OldStatus = previousStatus,
                NewStatus = nextStatus,
                HasCreatedStockMovements = stockMovementCount > 0,
                CreatedStockMovementCount = stockMovementCount,
                HasNotification = hasNotification
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Décrémente le stock de chaque article lié à un produit via le décrément atomique sûr
    /// (<see cref="IStockMutationService.DecrementStockAsync"/>) et crée le mouvement <c>Out</c> correspondant
    /// (<c>Quantity</c> <b>négative</b>, convention de signe P3-5). Un stock insuffisant lève
    /// <see cref="InsufficientStockException"/> ⇒ rollback complet par le runner (statut non avancé).
    /// </summary>
    private async Task<int> CreateStockMovementsForFabricationAsync(Order order, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var item in order.OrderItems.Where(i => i.ProductId.HasValue))
        {
            // Décrément atomique conditionnel : jamais négatif (contrairement à l'ancien `-=` non borné).
            await _stockMutationService.DecrementStockAsync(item.ProductId!.Value, item.Quantity, cancellationToken);

            var movement = new StockMovement
            {
                ProductId = item.ProductId!.Value,
                MovementType = StockMovementType.Out,
                Quantity = -item.Quantity, // sortie ⇒ delta négatif (convention P3-5).
                Reason = $"Fabrication commande {order.OrderNumber}",
                CreatedAt = DateTime.UtcNow,
            };

            await _stockMovementRepository.CreateAsync(movement, cancellationToken);
            count++;
        }

        return count;
    }
}
