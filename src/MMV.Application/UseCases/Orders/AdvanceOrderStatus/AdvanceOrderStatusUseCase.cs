using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.AdvanceOrderStatus;

/// <summary>
/// Implémentation du use case « Faire avancer le statut / réception d'une commande » (P2B-2E). Déplace, <b>sans
/// changement de comportement observable</b>, l'orchestration métier qui vivait dans
/// <c>OrderDetailViewModel.AdvanceStatusAsync</c> vers la couche Application. Réutilise telles quelles les
/// interfaces de persistance existantes (<see cref="IOrderRepository"/>, <see cref="IStockMovementRepository"/>,
/// <see cref="IUnitOfWork"/>, <see cref="INotificationRepository"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Pas de <c>ITransactionRunner</c>.</b> Le flux d'origine effectue plusieurs modifications d'entités (mise à
/// jour du statut, mouvements de stock, décrément du stock produit, notification) mais les valide par un
/// <b>unique</b> <c>SaveChangesAsync</c> final — déjà atomique (EF enveloppe un <c>SaveChanges</c> dans sa propre
/// transaction implicite). Aucune séquence multi-<c>SaveChanges</c> à protéger : introduire une transaction
/// explicite serait une refonte, pas un déplacement. La frontière transactionnelle reste donc le
/// <c>SaveChangesAsync</c> unique, à l'identique (déplacement iso-fonctionnel).
/// </para>
/// <para>
/// <see cref="INotificationRepository"/> est <b>optionnel</b> (peut être <c>null</c>), reproduisant exactement la
/// garde <c>if (_notificationRepository != null)</c> du flux d'origine. Le décrément de stock conserve la
/// sémantique d'origine : mouvement <c>Out</c> de quantité <b>positive</b> et décrément direct de
/// <c>Product.StockQuantity</c> (aucun <c>IStockMutationService</c> n'était utilisé par ce flux ; ne pas en
/// introduire).
/// </para>
/// </remarks>
public sealed class AdvanceOrderStatusUseCase : IAdvanceOrderStatusUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationRepository? _notificationRepository;

    public AdvanceOrderStatusUseCase(
        IOrderRepository orderRepository,
        IStockMovementRepository stockMovementRepository,
        IUnitOfWork unitOfWork,
        INotificationRepository? notificationRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Notification optionnelle (comme le flux d'origine) : si null, aucune notification n'est créée.
        _notificationRepository = notificationRepository;
    }

    /// <inheritdoc />
    public async Task<AdvanceOrderStatusResult> ExecuteAsync(AdvanceOrderStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var previousStatus = command.CurrentStatus;
        var nextStatus = command.NextStatus;

        // Recharger la commande fraîche pour éviter les conflits EF (iso-fonctionnel).
        var fresh = await _orderRepository.GetWithItemsAsync(command.OrderId, cancellationToken);
        if (fresh is null)
        {
            // « Commande introuvable. » côté ViewModel : aucun changement d'état, aucune écriture.
            return new AdvanceOrderStatusResult { OrderFound = false };
        }

        fresh.Status = nextStatus;
        await _orderRepository.UpdateAsync(fresh, cancellationToken);

        // Sortie de stock lors du passage en fabrication (À fabriquer → En fabrication).
        var stockMovementCount = 0;
        if (previousStatus == OrderStatus.ToFabricate && nextStatus == OrderStatus.InProgress)
        {
            stockMovementCount = await CreateStockMovementsForFabricationAsync(fresh, cancellationToken);
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
            await _notificationRepository.CreateAsync(notification, cancellationToken);
            hasNotification = true;
        }

        // Écriture unique (atomique) : statut + mouvements de stock + décréments + notification.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
    }

    /// <summary>
    /// Crée un mouvement de stock de type <c>Out</c> pour chaque article de la commande disposant d'un produit,
    /// et décrémente directement le stock du produit chargé. Port iso-fonctionnel de l'ancien
    /// <c>CreateStockMovementsForFabrication</c> (l'enregistrement est effectué par l'appelant).
    /// </summary>
    private async Task<int> CreateStockMovementsForFabricationAsync(Order order, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var item in order.OrderItems.Where(i => i.ProductId.HasValue))
        {
            var movement = new StockMovement
            {
                ProductId = item.ProductId!.Value,
                MovementType = StockMovementType.Out,
                Quantity = item.Quantity,
                Reason = $"Fabrication commande {order.OrderNumber}",
                CreatedAt = DateTime.UtcNow,
            };

            await _stockMovementRepository.CreateAsync(movement, cancellationToken);

            // Mettre à jour le stock du produit (le SaveChanges est fait au niveau appelant).
            if (item.Product != null)
            {
                item.Product.StockQuantity -= item.Quantity;
            }

            count++;
        }

        return count;
    }
}
