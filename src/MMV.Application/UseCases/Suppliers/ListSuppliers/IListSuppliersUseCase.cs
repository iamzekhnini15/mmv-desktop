using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Suppliers.ListSuppliers;

/// <summary>
/// Query use case « Lister les fournisseurs » (P2D-2). Remplace <c>ISupplierRepository.GetAllAsync</c> côté UI.
/// </summary>
public interface IListSuppliersUseCase
{
    /// <summary>Renvoie tous les fournisseurs, projetés en <see cref="SupplierListItemDto"/> (jamais des entités EF).</summary>
    Task<IReadOnlyList<SupplierListItemDto>> ExecuteAsync(ListSuppliersQuery query, CancellationToken cancellationToken = default);
}
