using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des ventes.
/// </summary>
public class SaleRepository : BaseRepository<Sale, long>, ISaleRepository
{
    public SaleRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère toutes les ventes avec leurs articles (SaleItems).
    /// </summary>
    public async Task<IList<Sale>> GetAllWithItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Sales
            .Include(s => s.SaleItems)
            .Include(s => s.Customer)
            .Include(s => s.Staff)
            .AsNoTracking()
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère une vente avec tous ses articles.
    /// </summary>
    public async Task<Sale?> GetWithItemsAsync(long saleId, CancellationToken cancellationToken = default)
    {
        return await _context.Sales
            .Include(s => s.SaleItems)
            .Include(s => s.Customer)
            .Include(s => s.Staff)
            .FirstOrDefaultAsync(s => s.SaleId == saleId, cancellationToken);
    }

    /// <summary>
    /// Recherche une vente par son numéro.
    /// </summary>
    public async Task<Sale?> GetBySaleNumberAsync(string saleNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saleNumber);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(s => s.SaleNumber == saleNumber, cancellationToken);
    }

    /// <summary>
    /// Récupère les ventes d'un client.
    /// </summary>
    public async Task<IList<Sale>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Indique si le client possède au moins une vente (P3-2B). <c>AnyAsync</c> : la requête s'arrête à la
    /// première ligne trouvée et ne matérialise aucune entité.
    /// </summary>
    public async Task<bool> ExistsByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .AnyAsync(s => s.CustomerId == customerId, cancellationToken);
    }

    /// <summary>
    /// Récupère les ventes créées entre deux dates.
    /// </summary>
    public async Task<IList<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate)
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Calcule le total des ventes pour une période.
    /// </summary>
    public async Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(s => s.SaleDate >= startDate && s.SaleDate <= endDate)
            .SumAsync(s => s.FinalAmount, cancellationToken);
    }

    /// <summary>
    /// Récupère les ventes par méthode de paiement.
    /// </summary>
    public async Task<IList<Sale>> GetByPaymentMethodAsync(PaymentMethod paymentMethod, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(s => s.PaymentMethod == paymentMethod)
            .OrderByDescending(s => s.SaleDate)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TrySettleRemainingBalanceAsync(long saleId, CancellationToken cancellationToken = default)
    {
        // Règlement atomique conditionnel (P3-7) : « il reste quelque chose à encaisser » est évalué par la MÊME
        // instruction que l'écriture. Deux postes réglant le même solde ne peuvent donc pas réussir tous les deux —
        // le second n'affecte aucune ligne, n'écrit rien et ne notifie rien.
        //
        // RemainingAmount étant nullable, une valeur NULL ne satisfait pas « > 0 » : une vente au solde inconnu
        // n'est jamais réglée à l'aveugle. Le montant encaissé n'est pas fourni par l'appelant : DepositAmount est
        // posé à la valeur de FinalAmount TELLE QU'ELLE EST EN BASE (jamais une valeur lue puis renvoyée), ce qui
        // interdit tout encaissement fondé sur un montant périmé.
        var rowsAffected = await _context.Sales
            .Where(s => s.SaleId == saleId && s.RemainingAmount > 0m)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.DepositAmount, s => (decimal?)s.FinalAmount)
                    .SetProperty(s => s.RemainingAmount, (decimal?)0m)
                    .SetProperty(s => s.PaymentStatus, PaymentStatus.Paid),
                cancellationToken);

        return rowsAffected == 1;
    }
}
