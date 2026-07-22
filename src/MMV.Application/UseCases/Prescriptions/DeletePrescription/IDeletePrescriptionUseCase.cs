using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Prescriptions.DeletePrescription;

/// <summary>
/// Cas d'utilisation « Supprimer une ordonnance » (P2C-4), <b>devenu un point de refus</b> en P3-12 : il oppose la
/// règle de conservation de l'historique médical (<c>MMV.Domain.Policies.PrescriptionRetentionPolicy</c>) à toute
/// tentative de suppression physique.
/// </summary>
public interface IDeletePrescriptionUseCase
{
    /// <summary>
    /// Traite la demande de suppression décrite par <paramref name="command"/>. Si l'ordonnance est introuvable,
    /// aucune écriture n'est effectuée et le résultat porte
    /// <see cref="DeletePrescriptionResult.PrescriptionFound"/> = <c>false</c> (contrat P2C-4 inchangé). Si
    /// l'ordonnance existe, la demande est <b>refusée</b> : aucune valeur n'est renvoyée.
    /// </summary>
    /// <exception cref="MMV.Domain.Exceptions.BusinessRuleException">
    /// P3-12 — l'ordonnance existe : la suppression physique est refusée <b>sans aucune écriture</b>, quel que
    /// soit son état. Le message est stable
    /// (<c>MMV.Domain.Policies.PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage</c>). Ni archivage ni
    /// versionnement d'ordonnance n'existe : la conservation est le seul comportement offert.
    /// </exception>
    Task<DeletePrescriptionResult> ExecuteAsync(DeletePrescriptionCommand command, CancellationToken cancellationToken = default);
}
