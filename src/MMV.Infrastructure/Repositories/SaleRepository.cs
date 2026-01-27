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
}
