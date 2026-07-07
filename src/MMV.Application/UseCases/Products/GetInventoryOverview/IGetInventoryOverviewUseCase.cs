using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.GetInventoryOverview;

/// <summary>
/// Query use case « Obtenir l'état d'inventaire » (P2D-4). Remplace <c>IProductRepository.GetAllAsync</c> côté UI
/// dans <c>InventoryViewModel</c>.
/// </summary>
public interface IGetInventoryOverviewUseCase
{
    /// <summary>
    /// Renvoie tous les produits triés par nom, projetés en <see cref="InventoryProductItemDto"/> plats (jamais des
    /// entités EF).
    /// </summary>
    Task<IReadOnlyList<InventoryProductItemDto>> ExecuteAsync(GetInventoryOverviewQuery query, CancellationToken cancellationToken = default);
}
