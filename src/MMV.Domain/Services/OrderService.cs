using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Domain.Services;

/// <summary>
/// Service métier pour la gestion des commandes.
/// </summary>
public interface IOrderService
{
    Task<Order?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default);
    Task<IList<Order>> GetCustomerOrdersAsync(long customerId, CancellationToken cancellationToken = default);
    Task<IList<Order>> GetOrdersByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);
    Task<IList<Order>> GetOverdueOrdersAsync(CancellationToken cancellationToken = default);
    Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateOrderStatusAsync(long orderId, OrderStatus newStatus, CancellationToken cancellationToken = default);
}

public class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Order?> GetOrderAsync(long orderId, CancellationToken cancellationToken = default)
    {
        if (orderId <= 0) throw new ArgumentException("ID commande invalide.", nameof(orderId));
        return await _unitOfWork.Orders.GetWithItemsAsync(orderId, cancellationToken);
    }

    public async Task<IList<Order>> GetCustomerOrdersAsync(long customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentException("ID client invalide.", nameof(customerId));
        return await _unitOfWork.Orders.GetByCustomerIdAsync(customerId, cancellationToken);
    }

    public async Task<IList<Order>> GetOrdersByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Orders.GetByStatusAsync(status, cancellationToken);
    }

    public async Task<IList<Order>> GetOverdueOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await _unitOfWork.Orders.GetOverdueOrdersAsync(cancellationToken);
    }

    public async Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        
        if (order.OrderItems == null || order.OrderItems.Count == 0)
            throw new Exceptions.BusinessRuleException("Une commande doit contenir au moins un article.");
        
        order.OrderDate = DateTime.UtcNow;
        order.Status = OrderStatus.New;
        
        await _unitOfWork.Orders.CreateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        
        return order;
    }

    public async Task UpdateOrderStatusAsync(long orderId, OrderStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (orderId <= 0) throw new ArgumentException("ID commande invalide.", nameof(orderId));
        
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order == null) throw new Exceptions.EntityNotFoundException(nameof(Order), orderId);
        
        // Validation des transitions de statut
        if (!IsValidStatusTransition(order.Status, newStatus))
            throw new Exceptions.BusinessRuleException($"Transition invalide : {order.Status} → {newStatus}");
        
        order.Status = newStatus;
        await _unitOfWork.Orders.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static bool IsValidStatusTransition(OrderStatus current, OrderStatus next)
    {
        // Validation simplifiée du workflow
        return (current, next) switch
        {
            (OrderStatus.New, OrderStatus.ToFabricate) => true,
            (OrderStatus.ToFabricate, OrderStatus.InProgress) => true,
            (OrderStatus.InProgress, OrderStatus.QualityCheck) => true,
            (OrderStatus.QualityCheck, OrderStatus.Ready) => true,
            (OrderStatus.Ready, OrderStatus.Delivered) => true,
            _ => false
        };
    }
}
