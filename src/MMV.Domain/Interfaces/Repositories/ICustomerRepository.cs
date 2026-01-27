using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des clients.
/// </summary>
public interface ICustomerRepository : IGenericRepository<Customer, long>
{
    /// <summary>
    /// Recherche des clients par nom (partiel).
    /// </summary>
    Task<IList<Customer>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche un client par numéro de téléphone.
    /// </summary>
    Task<Customer?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche un client par email.
    /// </summary>
    Task<Customer?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère un client avec toutes ses prescriptions.
    /// </summary>
    Task<Customer?> GetWithPrescriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère un client avec tout son historique (commandes et ventes).
    /// </summary>
    Task<Customer?> GetWithHistoryAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère tous les clients créés entre deux dates.
    /// </summary>
    Task<IList<Customer>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
}
