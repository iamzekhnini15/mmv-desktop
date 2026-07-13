using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des ordonnances.
/// </summary>
public class PrescriptionRepository : BaseRepository<Prescription, long>, IPrescriptionRepository
{
    public PrescriptionRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère toutes les ordonnances d'un client.
    /// </summary>
    public async Task<IList<Prescription>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.IssueDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Indique si le client possède au moins une ordonnance (P3-2B). <c>AnyAsync</c> : la requête s'arrête à la
    /// première ligne trouvée et ne matérialise aucune entité.
    /// </summary>
    public async Task<bool> ExistsByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .AnyAsync(p => p.CustomerId == customerId, cancellationToken);
    }

    /// <summary>
    /// Récupère les ordonnances créées entre deux dates.
    /// </summary>
    public async Task<IList<Prescription>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.IssueDate >= startDate && p.IssueDate <= endDate)
            .OrderByDescending(p => p.IssueDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère la dernière ordonnance d'un client.
    /// </summary>
    public async Task<Prescription?> GetLatestByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.IssueDate)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
