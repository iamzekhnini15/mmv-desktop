using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Prescriptions.UpdatePrescription;

/// <summary>
/// Cas d'utilisation « Modifier une ordonnance » (P2C-4). Charge l'ordonnance par son identifiant, met à jour ses
/// champs éditables et persiste. Si l'ordonnance est introuvable, aucune écriture n'est effectuée
/// (<see cref="UpdatePrescriptionResult.PrescriptionFound"/> = <c>false</c>).
/// </summary>
public interface IUpdatePrescriptionUseCase
{
    /// <summary>
    /// Met à jour l'ordonnance décrite par <paramref name="command"/> et renvoie le résultat (présence, identifiant).
    /// Les erreurs techniques sont propagées telles quelles.
    /// </summary>
    Task<UpdatePrescriptionResult> ExecuteAsync(UpdatePrescriptionCommand command, CancellationToken cancellationToken = default);
}
