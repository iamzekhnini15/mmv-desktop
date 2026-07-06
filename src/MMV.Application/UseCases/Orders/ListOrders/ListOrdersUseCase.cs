using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.ListOrders;

/// <summary>
/// Implémentation du query use case « Lister les commandes » (P2D-6). Déplace, sans changement de comportement
/// observable, la lecture <c>IOrderRepository.GetAllWithItemsAsync</c> qu'effectuaient <c>OrdersListViewModel</c> et
/// <c>OrderKanbanViewModel</c>, en projetant chaque entité <c>Order</c> (et sa vente / son client) vers un
/// <see cref="OrderListItemDto"/> composite plat, et en conservant le tri décroissant par date appliqué par les deux
/// ViewModels. Aucune entité EF suivie ne franchit la frontière UI.
/// </summary>
public sealed class ListOrdersUseCase : IListOrdersUseCase
{
    private readonly IOrderRepository _orderRepository;

    public ListOrdersUseCase(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderListItemDto>> ExecuteAsync(ListOrdersQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var orders = await _orderRepository.GetAllWithItemsAsync(cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderListItemDto
            {
                OrderId = o.OrderId,
                OrderNumber = o.OrderNumber,
                Status = o.Status,
                OrderDate = o.OrderDate,
                EstimatedDelivery = o.EstimatedDelivery,
                Notes = o.Notes,
                Sale = o.Sale is null
                    ? null
                    : new OrderSaleSummaryDto
                    {
                        FinalAmount = o.Sale.FinalAmount,
                        DepositAmount = o.Sale.DepositAmount,
                        RemainingAmount = o.Sale.RemainingAmount,
                        Customer = o.Sale.Customer is null
                            ? null
                            : new OrderCustomerSummaryDto
                            {
                                FirstName = o.Sale.Customer.FirstName,
                                LastName = o.Sale.Customer.LastName,
                            },
                    },
            })
            .ToList();
    }
}
