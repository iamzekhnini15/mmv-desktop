using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Customers.DeleteCustomer;

/// <summary>
/// Cas d'utilisation « Supprimer un client » (P2C-3). Orchestre le chargement du client et sa suppression,
/// en remplacement de l'orchestration auparavant portée par <c>CustomersListViewModel.ExecuteDelete</c>
/// (appel direct <c>ICustomerRepository.DeleteAsync</c> + <c>IUnitOfWork.SaveChangesAsync</c>).
/// </summary>
public interface IDeleteCustomerUseCase
{
    /// <summary>
    /// Exécute la suppression du client décrit par <paramref name="command"/> et renvoie le résultat
    /// (présence, identifiant). Si le client est introuvable, aucune écriture n'est effectuée
    /// (<see cref="DeleteCustomerResult.CustomerFound"/> = <c>false</c>).
    /// </summary>
    /// <exception cref="MMV.Domain.Exceptions.BusinessRuleException">
    /// P3-2B — le client possède un historique (au moins une ordonnance <b>ou</b> au moins une vente) : la
    /// suppression physique est refusée sans aucune écriture. Le message est stable
    /// (<see cref="DeleteCustomerUseCase.CustomerHasHistoryMessage"/>) ; le client doit être archivé.
    /// </exception>
    Task<DeleteCustomerResult> ExecuteAsync(DeleteCustomerCommand command, CancellationToken cancellationToken = default);
}
