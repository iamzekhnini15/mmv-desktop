using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Prescriptions.UpdatePrescription;

/// <summary>
/// Implémentation du use case « Modifier une ordonnance » (P2C-4). Réutilise telles quelles les interfaces de
/// persistance existantes (<see cref="IPrescriptionRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Contexte.</b> Le formulaire d'ordonnance (<c>PrescriptionFormViewModel</c>) servait à la fois la création et
/// l'« édition », mais sa sauvegarde appelait <b>toujours</b> <c>CreateAsync</c> sans capturer l'identifiant : éditer
/// une ordonnance créait en réalité un doublon. P2C-4 introduit la vraie mise à jour (la ViewModel capture désormais
/// <c>PrescriptionId</c> et, en mode édition, délègue ici).
/// </para>
/// <para>
/// <b>Frontière transactionnelle.</b> Mono-écriture (<c>UpdateAsync</c> + un unique <c>SaveChangesAsync</c>) :
/// <c>ITransactionRunner</c> non requis (cohérent avec <c>UpdateOrderUseCase</c> / <c>UpdateCustomerUseCase</c>).
/// </para>
/// <para>
/// <b>Chargement &amp; champs préservés.</b> L'ordonnance est rechargée par son identifiant ; ses champs éditables
/// (date, médecin, valeurs OD/OG, notes) sont mis à jour. <c>CustomerId</c> et <c>CreatedAt</c> sont <b>préservés</b>
/// (une mise à jour ne réaffecte ni le propriétaire ni la date de création). L'entité n'a pas d'horodatage de
/// modification ; aucun champ technique n'est inventé. Champs optionnels non normalisés (cohérent avec la saisie).
/// </para>
/// </remarks>
public sealed class UpdatePrescriptionUseCase : IUpdatePrescriptionUseCase
{
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

        // CustomerId et CreatedAt préservés : seuls les champs éditables du formulaire sont mis à jour.
        prescription.IssueDate = command.IssueDate;
        prescription.DoctorName = command.DoctorName;
        prescription.OdSphere = command.OdSphere;
        prescription.OdCylinder = command.OdCylinder;
        prescription.OdAxis = command.OdAxis;
        prescription.OdAddition = command.OdAddition;
        prescription.OdPrismValue = command.OdPrismValue;
        prescription.OdPrismBase = command.OdPrismBase;
        prescription.OdVisualAcuity = command.OdVisualAcuity;
        prescription.OgSphere = command.OgSphere;
        prescription.OgCylinder = command.OgCylinder;
        prescription.OgAxis = command.OgAxis;
        prescription.OgAddition = command.OgAddition;
        prescription.OgPrismValue = command.OgPrismValue;
        prescription.OgPrismBase = command.OgPrismBase;
        prescription.OgVisualAcuity = command.OgVisualAcuity;
        prescription.Notes = command.Notes;

        await _prescriptionRepository.UpdateAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdatePrescriptionResult { PrescriptionFound = true, PrescriptionId = prescription.PrescriptionId };
    }
}
