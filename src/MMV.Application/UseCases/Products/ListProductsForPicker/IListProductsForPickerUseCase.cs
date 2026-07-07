using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.ListProductsForPicker;

/// <summary>
/// Query use case « Lister les produits pour un sélecteur » (P2D-4). Remplace <c>IProductRepository.GetAllAsync</c>
/// côté UI dans les sélecteurs produit du module Stock (<c>StockMovementsListViewModel</c> et
/// <c>StockMovementFormViewModel</c>).
/// </summary>
public interface IListProductsForPickerUseCase
{
    /// <summary>
    /// Renvoie tous les produits triés par nom, projetés en <see cref="ProductPickerItemDto"/> plats (jamais des
    /// entités EF).
    /// </summary>
    Task<IReadOnlyList<ProductPickerItemDto>> ExecuteAsync(ListProductsForPickerQuery query, CancellationToken cancellationToken = default);
}
