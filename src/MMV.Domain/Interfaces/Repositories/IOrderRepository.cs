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
    /// Récupère les commandes d'une vente spécifique.
    /// </summary>
    Task<IList<Order>> GetBySaleIdAsync(long saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prise de statut <b>atomique conditionnelle</b> (P3-5) : fait passer la commande <paramref name="orderId"/>
    /// de <paramref name="expectedStatus"/> à <paramref name="nextStatus"/> <b>uniquement si</b> le statut réellement
    /// stocké est encore <paramref name="expectedStatus"/>. Réalisée par une seule instruction
    /// <c>UPDATE … WHERE OrderId = @id AND Status = @expected</c> (aucune comparaison en mémoire), afin que deux
    /// postes tentant simultanément la même transition n'obtiennent qu'<b>une seule</b> réussite.
    /// </summary>
    /// <returns><c>true</c> si la transition a été prise (1 ligne affectée) ; <c>false</c> si le statut stocké ne
    /// correspondait plus (0 ligne : commande déjà avancée, rejouée, ou introuvable).</returns>
    Task<bool> TryTransitionStatusAsync(long orderId, OrderStatus expectedStatus, OrderStatus nextStatus, CancellationToken cancellationToken = default);

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
