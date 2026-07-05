using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Suppliers.UpdateSupplier;

/// <summary>
/// Cas d'utilisation « Modifier un fournisseur » (P2C-GLOBAL). Remplace la branche édition auparavant portée par
/// <c>SupplierFormViewModel.SaveAsync</c>.
/// </summary>
public interface IUpdateSupplierUseCase
{
    /// <summary>
    /// Applique les modifications décrites par <paramref name="command"/>. Si le fournisseur est introuvable,
    /// aucune écriture n'est effectuée (<see cref="UpdateSupplierResult.SupplierFound"/> = <c>false</c>).
    /// </summary>
    Task<UpdateSupplierResult> ExecuteAsync(UpdateSupplierCommand command, CancellationToken cancellationToken = default);
}
