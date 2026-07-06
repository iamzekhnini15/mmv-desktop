namespace MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

/// <summary>
/// Entrée (query) du use case <see cref="ListPrescriptionsByCustomerUseCase"/> — « Lister les ordonnances d'un
/// client » (P2D-5). Porte l'unique critère consommé par <c>CustomerPrescriptionsViewModel</c> : l'identifiant du
/// client. Le tri décroissant par date d'émission reste hérité du repository (iso-fonctionnel).
/// </summary>
public sealed class ListPrescriptionsByCustomerQuery
{
    /// <summary>Identifiant du client dont on charge les ordonnances.</summary>
    public long CustomerId { get; init; }
}
