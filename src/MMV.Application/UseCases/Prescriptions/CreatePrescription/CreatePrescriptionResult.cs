namespace MMV.Application.UseCases.Prescriptions.CreatePrescription;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreatePrescriptionUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (événement <c>PrescriptionSaved</c>, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
public sealed class CreatePrescriptionResult
{
    /// <summary>Identifiant de l'ordonnance créée (généré par la persistance).</summary>
    public long PrescriptionId { get; init; }
}
