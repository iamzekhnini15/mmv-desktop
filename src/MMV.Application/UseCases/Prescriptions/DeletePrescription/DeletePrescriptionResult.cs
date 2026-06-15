namespace MMV.Application.UseCases.Prescriptions.DeletePrescription;

/// <summary>
/// Sortie (DTO) du use case <see cref="DeletePrescriptionUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// <see cref="PrescriptionFound"/> indique si l'ordonnance visée existait. Dans le flux d'origine, la suppression
/// n'est déclenchée que sur une ordonnance réelle (donc <c>true</c> en pratique) ; ce drapeau couvre le cas
/// théorique d'une ordonnance introuvable sans écriture.
/// </remarks>
public sealed class DeletePrescriptionResult
{
    /// <summary>Indique si l'ordonnance à supprimer a été trouvée. <c>false</c> = aucune écriture effectuée.</summary>
    public bool PrescriptionFound { get; init; }

    /// <summary>Identifiant de l'ordonnance visée (écho de l'entrée).</summary>
    public long PrescriptionId { get; init; }
}
