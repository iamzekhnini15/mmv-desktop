namespace MMV.Application.UseCases.Prescriptions.UpdatePrescription;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdatePrescriptionUseCase"/>.
/// </summary>
/// <remarks>
/// <see cref="PrescriptionFound"/> indique si l'ordonnance visée existait. En mode édition, la ViewModel ouvre
/// toujours le formulaire avec une ordonnance réelle (donc <c>true</c> en pratique) ; ce drapeau couvre le cas
/// théorique d'une ordonnance introuvable sans écriture.
/// </remarks>
public sealed class UpdatePrescriptionResult
{
    /// <summary>Indique si l'ordonnance à modifier a été trouvée. <c>false</c> = aucune écriture effectuée.</summary>
    public bool PrescriptionFound { get; init; }

    /// <summary>Identifiant de l'ordonnance visée (écho de l'entrée).</summary>
    public long PrescriptionId { get; init; }
}
