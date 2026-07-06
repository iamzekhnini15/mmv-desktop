using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Suppliers.ListSuppliers;

/// <summary>
/// Implémentation du query use case « Lister les fournisseurs » (P2D-2). Déplace, sans changement de comportement
/// observable, la lecture de <c>SuppliersListViewModel.LoadSuppliersAsync</c> (<c>ISupplierRepository.GetAllAsync</c>),
/// en projetant chaque entité vers un <see cref="SupplierListItemDto"/> plat.
/// </summary>
public sealed class ListSuppliersUseCase : IListSuppliersUseCase
{
    private readonly ISupplierRepository _supplierRepository;

    public ListSuppliersUseCase(ISupplierRepository supplierRepository)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupplierListItemDto>> ExecuteAsync(ListSuppliersQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var suppliers = await _supplierRepository.GetAllAsync(cancellationToken);

        return suppliers.Select(s => new SupplierListItemDto
        {
            SupplierId = s.SupplierId,
            Name = s.Name,
            ContactEmail = s.ContactEmail,
            Phone = s.Phone,
            Address = s.Address,
            ReferenceCode = s.ReferenceCode,
        }).ToList();
    }
}
