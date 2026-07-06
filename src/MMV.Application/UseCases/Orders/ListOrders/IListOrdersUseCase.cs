using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.ListOrders;

/// <summary>
/// Query use case « Lister les commandes » (P2D-6). Remplace <c>IOrderRepository.GetAllWithItemsAsync</c> côté UI
/// pour les écrans de lecture du module Commandes (<c>OrdersListViewModel</c>, <c>OrderKanbanViewModel</c>).
/// </summary>
public interface IListOrdersUseCase
{
    /// <summary>
    /// Renvoie toutes les commandes (tri décroissant par date, comme les ViewModels d'origine), projetées en
    /// <see cref="OrderListItemDto"/> composites (jamais l'entité EF suivie <c>Order</c>).
    /// </summary>
    Task<IReadOnlyList<OrderListItemDto>> ExecuteAsync(ListOrdersQuery query, CancellationToken cancellationToken = default);
}
