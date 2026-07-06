using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

/// <summary>
/// Query use case « Lister les ordonnances d'un client » (P2D-5). Remplace
/// <c>IPrescriptionRepository.GetByCustomerIdAsync</c> côté UI (<c>CustomerPrescriptionsViewModel</c>).
/// </summary>
public interface IListPrescriptionsByCustomerUseCase
{
    /// <summary>
    /// Renvoie les ordonnances du client (tri décroissant par date d'émission, comme le repository), projetées en
    /// <see cref="PrescriptionListItemDto"/> plats (jamais l'entité EF <c>Prescription</c>).
    /// </summary>
    Task<IReadOnlyList<PrescriptionListItemDto>> ExecuteAsync(ListPrescriptionsByCustomerQuery query, CancellationToken cancellationToken = default);
}
