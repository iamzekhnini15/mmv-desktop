using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

/// <summary>
/// Implémentation du query use case « Fiche fournisseur avec ses produits » (P2D-2). Déplace, sans changement de
/// comportement observable, la lecture de <c>SuppliersViewModel.OnViewSupplierDetailsRequested</c>
/// (<c>ISupplierRepository.GetWithProductsAsync</c>). Projette le fournisseur et ses produits (triés par nom, comme
/// à l'affichage) vers un <see cref="SupplierDetailsDto"/> plat : aucune entité EF suivie ne franchit la frontière UI.
/// </summary>
public sealed class GetSupplierWithProductsUseCase : IGetSupplierWithProductsUseCase
{
    private readonly ISupplierRepository _supplierRepository;

    public GetSupplierWithProductsUseCase(ISupplierRepository supplierRepository)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
    }

    /// <inheritdoc />
    public async Task<SupplierDetailsDto?> ExecuteAsync(GetSupplierWithProductsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var supplier = await _supplierRepository.GetWithProductsAsync(query.SupplierId, cancellationToken);
        if (supplier is null) return null;

        return new SupplierDetailsDto
        {
            SupplierId = supplier.SupplierId,
            Name = supplier.Name,
            ContactEmail = supplier.ContactEmail,
            Phone = supplier.Phone,
            Address = supplier.Address,
            ReferenceCode = supplier.ReferenceCode,
            Products = (supplier.Products ?? Enumerable.Empty<Domain.Entities.Product>())
                .OrderBy(p => p.Name)
                .Select(p => new SupplierProductItemDto
                {
                    Reference = p.Reference,
                    Name = p.Name,
                    Category = p.Category,
                    SalePrice = p.SalePrice,
                })
                .ToList(),
        };
    }
}
