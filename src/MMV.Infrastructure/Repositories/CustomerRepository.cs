using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des clients.
/// </summary>
public class CustomerRepository : BaseRepository<Customer, long>, ICustomerRepository
{
    public CustomerRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère les clients, en excluant les archivés sauf demande explicite (P3-2B). Le filtre
    /// <c>IsArchived</c> est appliqué en SQL (pas de filtrage en mémoire).
    /// </summary>
    public async Task<IList<Customer>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var query = GetQueryable();

        if (!includeArchived)
        {
            query = query.Where(c => !c.IsArchived);
        }

        return await query.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recherche des clients par nom (partiel).
    /// </summary>
    public async Task<IList<Customer>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchTerm);
        
        var lowerSearchTerm = searchTerm.ToLower();
        
        return await GetQueryable()
            .Where(c => c.FirstName.ToLower().Contains(lowerSearchTerm) ||
                        c.LastName.ToLower().Contains(lowerSearchTerm))
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recherche un client par numéro de téléphone.
    /// </summary>
    public async Task<Customer?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(phone);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(c => c.Phone == phone, cancellationToken);
    }

    /// <summary>
    /// Recherche un client par email.
    /// </summary>
    public async Task<Customer?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(c => c.Email == email, cancellationToken);
    }

    /// <summary>
    /// Récupère un client avec toutes ses prescriptions.
    /// </summary>
    public async Task<Customer?> GetWithPrescriptionsAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .Include(c => c.Prescriptions)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
    }

    /// <summary>
    /// Récupère un client avec tout son historique (ventes).
    /// </summary>
    public async Task<Customer?> GetWithHistoryAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .Include(c => c.Prescriptions)
            .Include(c => c.Sales)
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
    }

    /// <summary>
    /// Récupère tous les clients créés entre deux dates.
    /// </summary>
    public async Task<IList<Customer>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireActiveAsync(long customerId, CancellationToken cancellationToken = default)
    {
        // Prise atomique conditionnelle (P3-7) : la condition « existe ET non archivé » est évaluée par la MÊME
        // instruction que l'écriture. La valeur écrite est celle que la ligne doit déjà porter (IsArchived = false)
        // : la mise à jour ne change aucune donnée métier, elle sert exclusivement de prise de ligne, maintenue
        // jusqu'au commit de la transaction ouverte par ITransactionRunner.
        //
        // ExecuteUpdateAsync contourne le change tracker (écriture directe en base, aucune entité suivie mutée) et
        // s'exécute sur le DbContext de la portée, donc dans la transaction courante : un rollback de la vente
        // libère la prise.
        var rowsAffected = await _context.Customers
            .Where(c => c.CustomerId == customerId && !c.IsArchived)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.IsArchived, false),
                cancellationToken);

        return rowsAffected == 1;
    }

    /// <inheritdoc />
    public async Task<Customer?> GetByIdFreshAsync(long customerId, CancellationToken cancellationToken = default)
    {
        // AsNoTracking : contourne délibérément le change tracker. GetByIdAsync (FindAsync hérité) renverrait
        // sinon une entité déjà suivie dans le DbContext de la portée sans requêter la base (P3-7, revue avant
        // commit) — un risque réel dès lors qu'un même contexte peut avoir chargé ce client avec tracking ailleurs
        // dans la même portée (p. ex. un écran client resté ouvert).
        return await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
    }
}
