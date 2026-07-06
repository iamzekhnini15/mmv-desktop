using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Suppliers.CreateSupplier;

/// <summary>
/// Cas d'utilisation « Créer un fournisseur » (P2C-GLOBAL). Orchestre la création et la persistance d'un
/// fournisseur, en remplacement de la branche création auparavant portée par
/// <c>SupplierFormViewModel.SaveAsync</c> (appel direct <c>ISupplierRepository.CreateAsync</c> +
/// <c>IUnitOfWork.SaveChangesAsync</c>).
/// </summary>
public interface ICreateSupplierUseCase
{
    /// <summary>
    /// Crée le fournisseur décrit par <paramref name="command"/> et renvoie son identifiant. Les erreurs
    /// techniques sont propagées telles quelles, exactement comme le flux d'origine.
    /// </summary>
    Task<CreateSupplierResult> ExecuteAsync(CreateSupplierCommand command, CancellationToken cancellationToken = default);
}
