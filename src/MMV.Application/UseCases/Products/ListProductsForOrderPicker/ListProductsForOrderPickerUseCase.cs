using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.ListProductsForOrderPicker;

/// <summary>
/// Implémentation du query use case « Lister les produits pour le sélecteur de commande » (P2D-6). Déplace, sans
/// changement de comportement observable, la lecture <c>IProductRepository.GetAllAsync</c> qu'effectuait
/// <c>OrderFormViewModel.InitializeAsync</c> pour peupler son catalogue de produits, en projetant chaque entité
/// <c>Product</c> vers un <see cref="OrderProductPickerDto"/> plat (avec prix de vente et catégorie) et en conservant
/// le tri par nom appliqué par le formulaire. Aucun filtre supplémentaire n'est appliqué (le formulaire de commande
/// filtrait par type d'article en présentation, iso-fonctionnel).
/// </summary>
public sealed class ListProductsForOrderPickerUseCase : IListProductsForOrderPickerUseCase
{
    private readonly IProductRepository _productRepository;

    public ListProductsForOrderPickerUseCase(IProductRepository productRepository)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderProductPickerDto>> ExecuteAsync(ListProductsForOrderPickerQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var products = await _productRepository.GetAllAsync(cancellationToken);

        return products
            .OrderBy(p => p.Name)
            .Select(p => new OrderProductPickerDto
            {
                ProductId = p.ProductId,
                Reference = p.Reference,
                Name = p.Name,
                SalePrice = p.SalePrice,
                Category = p.Category,
            })
            .ToList();
    }
}
