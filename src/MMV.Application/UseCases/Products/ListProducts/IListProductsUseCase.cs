using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Query use case « Lister les produits » (P2D-7D). Remplace <c>IProductRepository.GetAllAsync</c> côté UI
/// (<c>ProductsListViewModel</c>, alimentant aussi la fiche détaillée et le formulaire d'édition).
/// </summary>
public interface IListProductsUseCase
{
    /// <summary>
    /// Renvoie tous les produits (avec détails de catégorie et historique de commandes), projetés en
    /// <see cref="ProductListItemDto"/> plats (jamais l'entité EF <c>Product</c>). Recherche/filtre/pagination
    /// restent en présentation (iso-fonctionnel).
    /// </summary>
    Task<IReadOnlyList<ProductListItemDto>> ExecuteAsync(ListProductsQuery query, CancellationToken cancellationToken = default);
}
