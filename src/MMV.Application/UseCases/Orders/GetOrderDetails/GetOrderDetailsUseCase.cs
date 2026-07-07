using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Implémentation du query use case « Charger la fiche détaillée d'une commande » (P2D-7B). Déplace, sans
/// changement de comportement observable, la lecture portée jusque-là par
/// <c>OrdersViewModel.OnViewOrderDetail</c> (<c>IOrderRepository.GetWithItemsAsync</c>), en projetant l'entité vers
/// un <see cref="OrderDetailsDto"/> composite.
/// </summary>
public sealed class GetOrderDetailsUseCase : IGetOrderDetailsUseCase
{
    private readonly IOrderRepository _orderRepository;

    public GetOrderDetailsUseCase(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    /// <inheritdoc />
    public async Task<OrderDetailsDto?> ExecuteAsync(GetOrderDetailsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var order = await _orderRepository.GetWithItemsAsync(query.OrderId, cancellationToken);
        return order is null ? null : MapToDto(order);
    }

    /// <summary>
    /// Projette une entité <c>Order</c> (avec sa vente, son client et ses articles) vers un
    /// <see cref="OrderDetailsDto"/> plat. Public : également réutilisé par <c>OrderDetailViewModel</c> pour
    /// aligner l'état affiché après un résultat de use case de commande (<c>AdvanceOrderStatusResult.Order</c> /
    /// <c>SettleOrderBalanceResult.Order</c>, entités inchangées par P2D-7) sur le même DTO que la lecture initiale.
    /// </summary>
    public static OrderDetailsDto MapToDto(MMV.Domain.Entities.Order order)
    {
        return new OrderDetailsDto
        {
            OrderId = order.OrderId,
            OrderNumber = order.OrderNumber,
            SaleId = order.SaleId,
            OrderDate = order.OrderDate,
            EstimatedDelivery = order.EstimatedDelivery,
            Status = order.Status,
            Notes = order.Notes,
            Sale = order.Sale is null
                ? null
                : new OrderDetailsSaleDto
                {
                    CustomerId = order.Sale.CustomerId,
                    FinalAmount = order.Sale.FinalAmount,
                    DepositAmount = order.Sale.DepositAmount,
                    RemainingAmount = order.Sale.RemainingAmount,
                    PaymentMethod = order.Sale.PaymentMethod,
                    Customer = order.Sale.Customer is null
                        ? null
                        : new OrderDetailsCustomerDto
                        {
                            FirstName = order.Sale.Customer.FirstName,
                            LastName = order.Sale.Customer.LastName,
                            Phone = order.Sale.Customer.Phone,
                        },
                },
            Items = (order.OrderItems ?? Array.Empty<MMV.Domain.Entities.OrderItem>())
                .Select(i => new OrderDetailsItemDto
                {
                    OrderItemId = i.OrderItemId,
                    ProductId = i.ProductId,
                    ItemType = i.ItemType,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice,
                    Sphere = i.Sphere,
                    Cylinder = i.Cylinder,
                    Axis = i.Axis,
                    Addition = i.Addition,
                    Product = i.Product is null
                        ? null
                        : new OrderDetailsProductDto
                        {
                            Name = i.Product.Name,
                            Reference = i.Product.Reference,
                            Category = i.Product.Category,
                        },
                })
                .ToList(),
        };
    }
}
