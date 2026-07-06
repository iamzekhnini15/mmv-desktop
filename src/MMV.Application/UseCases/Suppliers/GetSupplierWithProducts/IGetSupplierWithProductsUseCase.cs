using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

/// <summary>
/// Query use case « Fiche fournisseur avec ses produits » (P2D-2). Remplace
/// <c>ISupplierRepository.GetWithProductsAsync</c> côté UI.
/// </summary>
public interface IGetSupplierWithProductsUseCase
{
    /// <summary>
    /// Renvoie la fiche du fournisseur (projetée en <see cref="SupplierDetailsDto"/>) ou <c>null</c> s'il est
    /// introuvable. Aucune entité EF suivie ne franchit la frontière UI.
    /// </summary>
    Task<SupplierDetailsDto?> ExecuteAsync(GetSupplierWithProductsQuery query, CancellationToken cancellationToken = default);
}
