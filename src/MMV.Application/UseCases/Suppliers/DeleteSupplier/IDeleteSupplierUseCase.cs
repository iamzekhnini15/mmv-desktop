using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Suppliers.DeleteSupplier;

/// <summary>
/// Cas d'utilisation « Supprimer un fournisseur » (P2C-GLOBAL). Remplace l'orchestration auparavant portée par
/// <c>SuppliersViewModel.OnDetailDeleteRequested</c> (appel direct <c>ISupplierRepository.DeleteAsync</c> +
/// <c>IUnitOfWork.SaveChangesAsync</c>).
/// </summary>
public interface IDeleteSupplierUseCase
{
    /// <summary>
    /// Supprime le fournisseur décrit par <paramref name="command"/>. Si le fournisseur est introuvable, aucune
    /// écriture n'est effectuée (<see cref="DeleteSupplierResult.SupplierFound"/> = <c>false</c>).
    /// </summary>
    Task<DeleteSupplierResult> ExecuteAsync(DeleteSupplierCommand command, CancellationToken cancellationToken = default);
}
