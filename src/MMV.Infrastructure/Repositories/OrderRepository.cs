using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des commandes.
/// </summary>
public class OrderRepository : BaseRepository<Order, long>, IOrderRepository
{
    public OrderRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère toutes les commandes avec leurs articles (OrderItems).
    /// </summary>
    public async Task<IList<Order>> GetAllWithItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Customer)
            .Include(o => o.Staff)
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère une commande avec tous ses articles.
    /// </summary>
    public async Task<Order?> GetWithItemsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Customer)
            .Include(o => o.Staff)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);
    }

    /// <summary>
    /// Recherche une commande par son numéro.
    /// </summary>
    public async Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderNumber);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes d'un client.
    /// </summary>
    public async Task<IList<Order>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes par statut.
    /// </summary>
    public async Task<IList<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.Status == status)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes créées entre deux dates.
    /// </summary>
    public async Task<IList<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes en retard (dépassé la date estimée de livraison).
    /// </summary>
    public async Task<IList<Order>> GetOverdueOrdersAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.EstimatedDelivery.HasValue && 
                        o.EstimatedDelivery < DateTime.Now && 
                        o.Status != OrderStatus.Delivered)
            .OrderBy(o => o.EstimatedDelivery)
            .ToListAsync(cancellationToken);
    }
}
