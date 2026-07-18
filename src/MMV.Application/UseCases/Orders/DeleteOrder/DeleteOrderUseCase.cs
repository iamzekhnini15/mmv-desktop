using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.DeleteOrder;

/// <summary>
/// Implémentation du use case « Supprimer une commande » (P2B-2H). Déplace, <b>sans changement de
/// comportement observable</b>, la suppression qui vivait dans <c>OrdersViewModel.OnDeleteOrderRequested</c>
/// vers la couche Application. Réutilise telles quelles les interfaces de persistance existantes
/// (<see cref="IOrderRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue une seule suppression suivie d'un seul
/// <c>SaveChangesAsync</c> — opération mono-écriture intrinsèquement atomique. <see cref="ITransactionRunner"/>
/// n'est donc <b>pas</b> nécessaire ici (contrairement aux use cases multi-écritures comme
/// <c>SettleOrderBalanceUseCase</c>). Cette absence est volontaire et documentée.
/// </para>
/// <para>
/// <b>Comportement introuvable.</b> Le <see cref="BaseRepository{TEntity,TId}.DeleteAsync(TId)"/> du flux
/// d'origine ne levait pas d'exception si la commande était absente (suppression silencieuse). Ce use case
/// adopte le même contrat : si la commande n'existe pas, il renvoie <see cref="DeleteOrderResult.OrderFound"/>
/// = <c>false</c> sans écrire. La ViewModel préserve son comportement (fermeture + rafraîchissement) dans les
/// deux cas.
/// </para>
/// </remarks>
public sealed class DeleteOrderUseCase : IDeleteOrderUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteOrderUseCase(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<DeleteOrderResult> ExecuteAsync(DeleteOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Charger l'entité avant suppression : évite la double recherche interne de BaseRepository.DeleteAsync(id)
        // et permet de reporter OrderFound précisément dans le résultat.
        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
            return new DeleteOrderResult { OrderFound = false, OrderId = command.OrderId };

        // Garde métier P3-6 : seule une commande au statut « Nouvelle » (brouillon initial) peut être supprimée
        // physiquement. Dès ToFabricate, la commande appartient à l'historique du workflow atelier ; à partir de
        // InProgress le stock peut déjà avoir été décrémenté. Supprimer une commande avancée effacerait ses lignes
        // optiques et laisserait des mouvements/notifications sans commande de rattachement. Aucun statut Cancelled,
        // aucun archivage, aucune restauration de stock (décisions reportées) : le refus est simplement une garde.
        if (order.Status != OrderStatus.New)
            throw new BusinessRuleException("Seule une commande au statut « Nouvelle » peut être supprimée.");

        // Supprimer par entité (iso-fonctionnel : même comportement que l'ancien DeleteAsync(id) qui chargeait
        // l'entité en interne avant de marquer la suppression).
        await _orderRepository.DeleteAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteOrderResult { OrderFound = true, OrderId = command.OrderId };
    }
}
