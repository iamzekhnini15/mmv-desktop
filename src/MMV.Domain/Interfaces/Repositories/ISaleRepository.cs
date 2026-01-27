using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des ventes.
/// </summary>
public interface ISaleRepository : IGenericRepository<Sale, long>
{
    /// <summary>
    /// Récupère toutes les ventes avec leurs articles (SaleItems).
    /// </summary>
    Task<IList<Sale>> GetAllWithItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère une vente avec tous ses articles.
    /// </summary>
    Task<Sale?> GetWithItemsAsync(long saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche une vente par son numéro.
    /// </summary>
    Task<Sale?> GetBySaleNumberAsync(string saleNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ventes d'un client.
    /// </summary>
    Task<IList<Sale>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ventes créées entre deux dates.
    /// </summary>
    Task<IList<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calcule le total des ventes pour une période.
    /// </summary>
    Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ventes par méthode de paiement.
    /// </summary>
    Task<IList<Sale>> GetByPaymentMethodAsync(PaymentMethod paymentMethod, CancellationToken cancellationToken = default);
}
