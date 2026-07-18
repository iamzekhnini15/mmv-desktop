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
    /// Récupère toutes les commandes avec leurs articles (OrderItems) et la vente parente.
    /// </summary>
    public async Task<IList<Order>> GetAllWithItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.Sale)
                .ThenInclude(s => s!.Customer)
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère une commande avec tous ses articles et la vente parente.
    /// </summary>
    public async Task<Order?> GetWithItemsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.Sale)
                .ThenInclude(s => s!.Customer)
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
    /// Récupère les commandes d'une vente spécifique.
    /// </summary>
    public async Task<IList<Order>> GetBySaleIdAsync(long saleId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.SaleId == saleId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Prise de statut atomique conditionnelle (P3-5) : un unique <c>UPDATE … WHERE OrderId = @id AND Status = @expected</c>
    /// via <c>ExecuteUpdateAsync</c>. La base décide en une seule instruction ; <c>rows == 1</c> ⇒ transition prise,
    /// <c>rows == 0</c> ⇒ statut stocké différent (déjà avancé, rejoué, ou commande introuvable). Contourne le change
    /// tracker : aucune entité suivie n'est réécrite, donc pas de double avancement.
    /// </summary>
    public async Task<bool> TryTransitionStatusAsync(long orderId, OrderStatus expectedStatus, OrderStatus nextStatus, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _context.Orders
            .Where(o => o.OrderId == orderId && o.Status == expectedStatus)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.Status, nextStatus),
                cancellationToken);

        return rowsAffected == 1;
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
