using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.GetInventoryOverview;

/// <summary>
/// Implémentation du query use case « Obtenir l'état d'inventaire » (P2D-4). Déplace, sans changement de
/// comportement observable, la lecture de <c>InventoryViewModel.LoadInventoryAsync</c>
/// (<c>IProductRepository.GetAllAsync</c>, tri par nom), en projetant chaque entité vers un
/// <see cref="InventoryProductItemDto"/> plat. Ne décide d'aucune règle métier : l'ajustement de stock reste porté
/// par <c>ICreateStockMovementUseCase</c> (inchangé).
/// </summary>
public sealed class GetInventoryOverviewUseCase : IGetInventoryOverviewUseCase
{
    private readonly IProductRepository _productRepository;

    public GetInventoryOverviewUseCase(IProductRepository productRepository)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryProductItemDto>> ExecuteAsync(GetInventoryOverviewQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var products = await _productRepository.GetAllAsync(cancellationToken);

        return products
            .OrderBy(p => p.Name)
            .Select(p => new InventoryProductItemDto
            {
                ProductId = p.ProductId,
                Reference = p.Reference,
                Name = p.Name,
                Category = p.Category,
                StockQuantity = p.StockQuantity,
            })
            .ToList();
    }
}
