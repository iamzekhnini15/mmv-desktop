using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

/// <summary>
/// Implémentation du query use case « Lister les ordonnances d'un client » (P2D-5). Déplace, sans changement de
/// comportement observable, la lecture portée jusque-là par <c>CustomerPrescriptionsViewModel.LoadPrescriptionsAsync</c>
/// (<c>IPrescriptionRepository.GetByCustomerIdAsync</c> — tri décroissant par date d'émission), en projetant chaque
/// entité <c>Prescription</c> vers un <see cref="PrescriptionListItemDto"/> plat.
/// </summary>
public sealed class ListPrescriptionsByCustomerUseCase : IListPrescriptionsByCustomerUseCase
{
    private readonly IPrescriptionRepository _prescriptionRepository;

    public ListPrescriptionsByCustomerUseCase(IPrescriptionRepository prescriptionRepository)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrescriptionListItemDto>> ExecuteAsync(ListPrescriptionsByCustomerQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var prescriptions = await _prescriptionRepository.GetByCustomerIdAsync(query.CustomerId, cancellationToken);

        return prescriptions.Select(p => new PrescriptionListItemDto
        {
            PrescriptionId = p.PrescriptionId,
            CustomerId = p.CustomerId,
            IssueDate = p.IssueDate,
            DoctorName = p.DoctorName,
            OdSphere = p.OdSphere,
            OdCylinder = p.OdCylinder,
            OdAxis = p.OdAxis,
            OdAddition = p.OdAddition,
            OdPrismValue = p.OdPrismValue,
            OdPrismBase = p.OdPrismBase,
            OdVisualAcuity = p.OdVisualAcuity,
            OgSphere = p.OgSphere,
            OgCylinder = p.OgCylinder,
            OgAxis = p.OgAxis,
            OgAddition = p.OgAddition,
            OgPrismValue = p.OgPrismValue,
            OgPrismBase = p.OgPrismBase,
            OgVisualAcuity = p.OgVisualAcuity,
            Notes = p.Notes,
        }).ToList();
    }
}
