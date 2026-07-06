using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.ListProductsForOrderPicker;

/// <summary>
/// Query use case « Lister les produits pour le sélecteur de commande » (P2D-6). Remplace
/// <c>IProductRepository.GetAllAsync</c> côté UI pour le sélecteur d'article du formulaire de commande
/// (<c>OrderFormViewModel</c>).
/// </summary>
public interface IListProductsForOrderPickerUseCase
{
    /// <summary>
    /// Renvoie les produits (tri par nom, comme le formulaire d'origine), projetés en
    /// <see cref="OrderProductPickerDto"/> plats (jamais l'entité EF <c>Product</c>).
    /// </summary>
    Task<IReadOnlyList<OrderProductPickerDto>> ExecuteAsync(ListProductsForOrderPickerQuery query, CancellationToken cancellationToken = default);
}
