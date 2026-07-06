using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Stock.ListStockMovements;

/// <summary>
/// Implémentation du query use case « Lister les mouvements de stock » (P2D-4). Déplace, sans changement de
/// comportement observable, la lecture de <c>StockMovementsListViewModel.LoadMovementsAsync</c>
/// (<c>IStockMovementRepository.GetAllAsync</c> — <c>AsNoTracking</c>, <c>Include(Product)</c>, tri décroissant par
/// date), en projetant chaque entité vers un <see cref="StockMovementListItemDto"/> plat.
/// </summary>
public sealed class ListStockMovementsUseCase : IListStockMovementsUseCase
{
    private readonly IStockMovementRepository _stockMovementRepository;

    public ListStockMovementsUseCase(IStockMovementRepository stockMovementRepository)
    {
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockMovementListItemDto>> ExecuteAsync(ListStockMovementsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var movements = await _stockMovementRepository.GetAllAsync(cancellationToken);

        return movements.Select(m => new StockMovementListItemDto
        {
            ProductId = m.ProductId,
            Product = new StockMovementProductRefDto
            {
                Name = m.Product?.Name ?? string.Empty,
                Reference = m.Product?.Reference,
            },
            MovementType = m.MovementType,
            Quantity = m.Quantity,
            Reason = m.Reason,
            CreatedAt = m.CreatedAt,
        }).ToList();
    }
}
