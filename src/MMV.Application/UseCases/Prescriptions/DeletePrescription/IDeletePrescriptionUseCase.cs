using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Prescriptions.DeletePrescription;

/// <summary>
/// Cas d'utilisation « Supprimer une ordonnance » (P2C-4). Orchestre le chargement de l'ordonnance et sa suppression,
/// en remplacement de l'orchestration auparavant portée par <c>CustomerPrescriptionsViewModel.ExecuteDeleteAsync</c>
/// (appel direct <c>IPrescriptionRepository.DeleteAsync</c> + <c>IUnitOfWork.CommitAsync</c>).
/// </summary>
public interface IDeletePrescriptionUseCase
{
    /// <summary>
    /// Exécute la suppression de l'ordonnance décrite par <paramref name="command"/> et renvoie le résultat
    /// (présence, identifiant). Si l'ordonnance est introuvable, aucune écriture n'est effectuée
    /// (<see cref="DeletePrescriptionResult.PrescriptionFound"/> = <c>false</c>). Les erreurs techniques sont
    /// propagées telles quelles, exactement comme le flux d'origine.
    /// </summary>
    Task<DeletePrescriptionResult> ExecuteAsync(DeletePrescriptionCommand command, CancellationToken cancellationToken = default);
}
