using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Prescriptions.DeletePrescription;

/// <summary>
/// Implémentation du use case « Supprimer une ordonnance » (P2C-4). Déplace, <b>sans changement de comportement
/// observable</b>, la suppression qui vivait dans <c>CustomerPrescriptionsViewModel.ExecuteDeleteAsync</c> vers la
/// couche Application. Réutilise telles quelles les interfaces de persistance existantes
/// (<see cref="IPrescriptionRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Mono-écriture (<c>DeleteAsync</c> + un unique <c>SaveChangesAsync</c>) :
/// <c>ITransactionRunner</c> non requis (cohérent avec <c>DeleteOrderUseCase</c> / <c>DeleteCustomerUseCase</c>).
/// </para>
/// <para>
/// <b>Comportement introuvable.</b> Le flux d'origine appelait <c>DeleteAsync(id)</c> sans vérifier l'existence
/// préalable. Ce use case charge d'abord l'entité par son identifiant : si l'ordonnance n'existe pas, il renvoie
/// <see cref="DeletePrescriptionResult.PrescriptionFound"/> = <c>false</c> sans écrire (contrat sûr et explicite).
/// Aucune cascade métier nouvelle n'est introduite.
/// </para>
/// </remarks>
public sealed class DeletePrescriptionUseCase : IDeletePrescriptionUseCase
{
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePrescriptionUseCase(IPrescriptionRepository prescriptionRepository, IUnitOfWork unitOfWork)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<DeletePrescriptionResult> ExecuteAsync(DeletePrescriptionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var prescription = await _prescriptionRepository.GetByIdAsync(command.PrescriptionId, cancellationToken);
        if (prescription is null)
            return new DeletePrescriptionResult { PrescriptionFound = false, PrescriptionId = command.PrescriptionId };

        await _prescriptionRepository.DeleteAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeletePrescriptionResult { PrescriptionFound = true, PrescriptionId = command.PrescriptionId };
    }
}
