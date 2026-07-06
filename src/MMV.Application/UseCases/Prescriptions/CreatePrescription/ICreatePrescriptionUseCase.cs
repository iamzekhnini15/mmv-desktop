using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Prescriptions.CreatePrescription;

/// <summary>
/// Cas d'utilisation « Créer une ordonnance » (P2C-4). Orchestre la construction de l'entité et sa persistance,
/// en remplacement de l'orchestration auparavant portée par <c>PrescriptionFormViewModel.SavePrescriptionAsync</c>
/// (appel direct <c>IPrescriptionRepository.CreateAsync</c> + <c>IUnitOfWork.CommitAsync</c>).
/// </summary>
public interface ICreatePrescriptionUseCase
{
    /// <summary>
    /// Crée l'ordonnance décrite par <paramref name="command"/> et renvoie son identifiant. Les erreurs techniques
    /// sont propagées telles quelles, exactement comme le flux d'origine.
    /// </summary>
    Task<CreatePrescriptionResult> ExecuteAsync(CreatePrescriptionCommand command, CancellationToken cancellationToken = default);
}
