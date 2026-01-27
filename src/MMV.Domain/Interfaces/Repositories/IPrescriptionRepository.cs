using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des ordonnances.
/// </summary>
public interface IPrescriptionRepository : IGenericRepository<Prescription, long>
{
    /// <summary>
    /// Récupère toutes les ordonnances d'un client.
    /// </summary>
    Task<IList<Prescription>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ordonnances créées entre deux dates.
    /// </summary>
    Task<IList<Prescription>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère la dernière ordonnance d'un client.
    /// </summary>
    Task<Prescription?> GetLatestByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);
}
