using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Prescriptions.CreatePrescription;

/// <summary>
/// Implémentation du use case « Créer une ordonnance » (P2C-4). Déplace, <b>sans changement de comportement
/// observable</b>, l'orchestration de persistance qui vivait dans <c>PrescriptionFormViewModel.SavePrescriptionAsync</c>
/// vers la couche Application. Réutilise telles quelles les interfaces de persistance existantes
/// (<see cref="IPrescriptionRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue une <b>seule</b> écriture atomique
/// (<c>CreateAsync</c> + une unique validation de transaction) sans <c>ITransactionRunner</c> : aucun runner de
/// transaction n'est introduit (déplacement iso-fonctionnel, pas refonte).
/// </para>
/// <para>
/// <b>Normalisation.</b> Le flux d'origine ne normalisait <b>pas</b> les champs optionnels (pas de blanc → null) ;
/// ce comportement est conservé tel quel (les valeurs sont recopiées telles fournies). <c>CreatedAt</c> est laissé
/// à la valeur par défaut de l'entité (<see cref="DateTime.UtcNow"/> à la construction), exactement comme le flux
/// d'origine qui ne le renseignait pas.
/// </para>
/// </remarks>
public sealed class CreatePrescriptionUseCase : ICreatePrescriptionUseCase
{
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePrescriptionUseCase(IPrescriptionRepository prescriptionRepository, IUnitOfWork unitOfWork)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<CreatePrescriptionResult> ExecuteAsync(CreatePrescriptionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var prescription = new Prescription
        {
            CustomerId = command.CustomerId,
            IssueDate = command.IssueDate,
            DoctorName = command.DoctorName,
            OdSphere = command.OdSphere,
            OdCylinder = command.OdCylinder,
            OdAxis = command.OdAxis,
            OdAddition = command.OdAddition,
            OdPrismValue = command.OdPrismValue,
            OdPrismBase = command.OdPrismBase,
            OdVisualAcuity = command.OdVisualAcuity,
            OgSphere = command.OgSphere,
            OgCylinder = command.OgCylinder,
            OgAxis = command.OgAxis,
            OgAddition = command.OgAddition,
            OgPrismValue = command.OgPrismValue,
            OgPrismBase = command.OgPrismBase,
            OgVisualAcuity = command.OgVisualAcuity,
            Notes = command.Notes
        };

        await _prescriptionRepository.CreateAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatePrescriptionResult { PrescriptionId = prescription.PrescriptionId };
    }
}
