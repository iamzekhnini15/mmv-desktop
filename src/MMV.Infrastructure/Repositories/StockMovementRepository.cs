using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des mouvements de stock.
/// </summary>
public class StockMovementRepository : BaseRepository<StockMovement, long>, IStockMovementRepository
{
    public StockMovementRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère tous les mouvements avec les produits associés.
    /// </summary>
    public override async Task<IList<StockMovement>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Include(sm => sm.Product)
            .OrderByDescending(sm => sm.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère tous les mouvements d'un produit.
    /// </summary>
    public async Task<IList<StockMovement>> GetByProductIdAsync(long productId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(sm => sm.ProductId == productId)
            .OrderByDescending(sm => sm.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les mouvements entre deux dates.
    /// </summary>
    public async Task<IList<StockMovement>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(sm => sm.CreatedAt >= startDate && sm.CreatedAt <= endDate)
            .OrderByDescending(sm => sm.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les mouvements d'un type spécifique.
    /// </summary>
    public async Task<IList<StockMovement>> GetByMovementTypeAsync(StockMovementType movementType, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(sm => sm.MovementType == movementType)
            .OrderByDescending(sm => sm.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère l'historique complet des mouvements d'un produit.
    /// </summary>
    public async Task<IList<StockMovement>> GetProductHistoryAsync(long productId, DateTime? startDate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryable()
            .Where(sm => sm.ProductId == productId);

        if (startDate.HasValue)
        {
            query = query.Where(sm => sm.CreatedAt >= startDate.Value);
        }

        return await query
            .OrderByDescending(sm => sm.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
