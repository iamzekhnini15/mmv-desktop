using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.ListProductsForPicker;

/// <summary>
/// Implémentation du query use case « Lister les produits pour un sélecteur » (P2D-4). Déplace, sans changement de
/// comportement observable, les lectures <c>IProductRepository.GetAllAsync</c> qu'effectuaient
/// <c>StockMovementsListViewModel</c> et <c>StockMovementFormViewModel</c> pour peupler leurs sélecteurs produit,
/// en projetant chaque entité vers un <see cref="ProductPickerItemDto"/> plat et en conservant le tri par nom
/// appliqué par les deux ViewModels.
/// </summary>
public sealed class ListProductsForPickerUseCase : IListProductsForPickerUseCase
{
    private readonly IProductRepository _productRepository;

    public ListProductsForPickerUseCase(IProductRepository productRepository)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductPickerItemDto>> ExecuteAsync(ListProductsForPickerQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var products = await _productRepository.GetAllAsync(cancellationToken);

        return products
            .OrderBy(p => p.Name)
            .Select(p => new ProductPickerItemDto
            {
                ProductId = p.ProductId,
                Reference = p.Reference,
                Name = p.Name,
                SupplierName = p.Supplier?.Name,
                Description = p.Description,
            })
            .ToList();
    }
}
