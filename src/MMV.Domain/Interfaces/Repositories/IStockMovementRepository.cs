using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des mouvements de stock.
/// </summary>
public interface IStockMovementRepository : IGenericRepository<StockMovement, long>
{
    /// <summary>
    /// Récupère tous les mouvements d'un produit.
    /// </summary>
    Task<IList<StockMovement>> GetByProductIdAsync(long productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les mouvements entre deux dates.
    /// </summary>
    Task<IList<StockMovement>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les mouvements d'un type spécifique.
    /// </summary>
    Task<IList<StockMovement>> GetByMovementTypeAsync(StockMovementType movementType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère l'historique complet des mouvements d'un produit.
    /// </summary>
    Task<IList<StockMovement>> GetProductHistoryAsync(long productId, DateTime? startDate = null, CancellationToken cancellationToken = default);
}
