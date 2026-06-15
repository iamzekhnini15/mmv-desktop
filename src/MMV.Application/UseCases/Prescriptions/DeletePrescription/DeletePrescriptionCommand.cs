namespace MMV.Application.UseCases.Prescriptions.DeletePrescription;

/// <summary>
/// Entrée (DTO) du use case <see cref="DeletePrescriptionUseCase"/> — « Supprimer une ordonnance » (P2C-4). Porte
/// l'identifiant de l'ordonnance à supprimer, tel que sélectionné dans <c>CustomerPrescriptionsViewModel</c>.
/// </summary>
/// <remarks>
/// DTO neutre. Déplacement iso-fonctionnel de <c>CustomerPrescriptionsViewModel.ExecuteDeleteAsync</c> (qui appelait
/// directement <c>IPrescriptionRepository.DeleteAsync(id)</c> + <c>IUnitOfWork.CommitAsync</c>).
/// </remarks>
public sealed class DeletePrescriptionCommand
{
    /// <summary>Identifiant de l'ordonnance à supprimer.</summary>
    public long PrescriptionId { get; init; }
}
