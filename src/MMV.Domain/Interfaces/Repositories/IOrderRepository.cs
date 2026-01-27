using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des commandes.
/// </summary>
public interface IOrderRepository : IGenericRepository<Order, long>
{
    /// <summary>
    /// Récupère toutes les commandes avec leurs articles (OrderItems).
    /// </summary>
    Task<IList<Order>> GetAllWithItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère une commande avec tous ses articles.
    /// </summary>
    Task<Order?> GetWithItemsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche une commande par son numéro.
    /// </summary>
    Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes d'un client.
    /// </summary>
    Task<IList<Order>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes par statut.
    /// </summary>
    Task<IList<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes créées entre deux dates.
    /// </summary>
    Task<IList<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes en retard (dépassé la date estimée de livraison).
    /// </summary>
    Task<IList<Order>> GetOverdueOrdersAsync(CancellationToken cancellationToken = default);
}
