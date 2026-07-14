using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Optics;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Prescriptions.UpdatePrescription;

/// <summary>
/// Implémentation du use case « Modifier une ordonnance » (P2C-4, validé en <b>P3-3B</b>). Réutilise telles quelles
/// les interfaces de persistance existantes (<see cref="IPrescriptionRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Validation avant mutation (P3-3B).</b> Les règles optiques (<c>PrescriptionValidator</c>) sont exécutées sur un
/// <b>candidat séparé</b>, construit à partir de la commande et des champs préservés de l'ordonnance existante. Ce
/// n'est qu'une fois le candidat validé que l'entité <b>suivie</b> est mutée : un refus de validation laisse donc
/// l'entité suivie <b>intacte</b>, et non dans un état partiellement modifié.
/// </para>
/// <para>
/// <b>Client archivé : la correction reste permise.</b> Le client n'est délibérément <b>pas</b> chargé ici. Archiver
/// un client (P3-2B) est réversible et sans cérémonie : ce n'est ni une clôture comptable ni un verrou légal. Une
/// ordonnance mal saisie doit rester corrigeable sans obliger l'utilisateur à réactiver puis ré-archiver le client.
/// « Archivé » = plus de <b>nouvelle</b> activité (refusée dans <c>CreatePrescriptionUseCase</c>), pas « données
/// gelées ».
/// </para>
/// <para>
/// <b>Champs préservés.</b> <c>CustomerId</c> et <c>CreatedAt</c> sont conservés : une mise à jour ne réaffecte ni le
/// propriétaire ni la date de création. L'entité n'a pas d'horodatage de modification ; aucun champ technique n'est
/// inventé. Les axes sont mis sous forme canonique (<c>0 → 180</c>) après validation.
/// </para>
/// <para>
/// <b>Frontière transactionnelle.</b> Mono-écriture (<c>UpdateAsync</c> + un unique <c>SaveChangesAsync</c>) :
/// <c>ITransactionRunner</c> non requis (cohérent avec <c>UpdateOrderUseCase</c> / <c>UpdateCustomerUseCase</c>).
/// </para>
/// </remarks>
public sealed class UpdatePrescriptionUseCase : IUpdatePrescriptionUseCase
{
    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication, cf. ADR frontières §9).
    // Stateless et thread-safe : une seule instance partagée suffit.
    private static readonly PrescriptionValidator PrescriptionValidator = new();

    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePrescriptionUseCase(IPrescriptionRepository prescriptionRepository, IUnitOfWork unitOfWork)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<UpdatePrescriptionResult> ExecuteAsync(UpdatePrescriptionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var prescription = await _prescriptionRepository.GetByIdAsync(command.PrescriptionId, cancellationToken);
        if (prescription is null)
            return new UpdatePrescriptionResult { PrescriptionFound = false, PrescriptionId = command.PrescriptionId };

        // Candidat séparé : nouvelles valeurs + champs préservés de l'existant. L'entité suivie n'est pas touchée
        // tant que ce candidat n'est pas validé.
        var candidate = new Prescription
        {
            PrescriptionId = prescription.PrescriptionId,
            CustomerId = prescription.CustomerId,
            CreatedAt = prescription.CreatedAt,
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

        var validationErrors = CommandValidation.Validate(PrescriptionValidator, candidate);
        if (validationErrors.Count > 0)
            return new UpdatePrescriptionResult
            {
                PrescriptionFound = true,
                PrescriptionId = command.PrescriptionId,
                ValidationErrors = validationErrors
            };

        candidate.OdAxis = OpticalAxisNormalizer.NormalizeAxis(candidate.OdAxis);
        candidate.OgAxis = OpticalAxisNormalizer.NormalizeAxis(candidate.OgAxis);

        // Report des valeurs validées et normalisées vers l'entité suivie. CustomerId et CreatedAt ne sont jamais
        // réaffectés (ils n'ont d'ailleurs pas quitté l'entité).
        prescription.IssueDate = candidate.IssueDate;
        prescription.DoctorName = candidate.DoctorName;
        prescription.OdSphere = candidate.OdSphere;
        prescription.OdCylinder = candidate.OdCylinder;
        prescription.OdAxis = candidate.OdAxis;
        prescription.OdAddition = candidate.OdAddition;
        prescription.OdPrismValue = candidate.OdPrismValue;
        prescription.OdPrismBase = candidate.OdPrismBase;
        prescription.OdVisualAcuity = candidate.OdVisualAcuity;
        prescription.OgSphere = candidate.OgSphere;
        prescription.OgCylinder = candidate.OgCylinder;
        prescription.OgAxis = candidate.OgAxis;
        prescription.OgAddition = candidate.OgAddition;
        prescription.OgPrismValue = candidate.OgPrismValue;
        prescription.OgPrismBase = candidate.OgPrismBase;
        prescription.OgVisualAcuity = candidate.OgVisualAcuity;
        prescription.Notes = candidate.Notes;

        await _prescriptionRepository.UpdateAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdatePrescriptionResult { PrescriptionFound = true, PrescriptionId = prescription.PrescriptionId };
    }
}
