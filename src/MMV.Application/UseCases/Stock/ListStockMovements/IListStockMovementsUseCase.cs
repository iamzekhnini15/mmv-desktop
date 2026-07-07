using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Stock.ListStockMovements;

/// <summary>
/// Query use case « Lister les mouvements de stock » (P2D-4). Remplace <c>IStockMovementRepository.GetAllAsync</c>
/// côté UI (<c>StockMovementsListViewModel</c>).
/// </summary>
public interface IListStockMovementsUseCase
{
    /// <summary>
    /// Renvoie tous les mouvements de stock (tri décroissant par date, comme le repository), projetés en
    /// <see cref="StockMovementListItemDto"/> plats (jamais des entités EF).
    /// </summary>
    Task<IReadOnlyList<StockMovementListItemDto>> ExecuteAsync(ListStockMovementsQuery query, CancellationToken cancellationToken = default);
}
