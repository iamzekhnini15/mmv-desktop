using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Optics;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Prescriptions.CreatePrescription;

/// <summary>
/// Implémentation du use case « Créer une ordonnance » (P2C-4, sécurisé en <b>P3-3B</b>). Orchestration de la
/// persistance extraite de <c>PrescriptionFormViewModel.SavePrescriptionAsync</c>, à laquelle P3-3B ajoute les
/// garde-fous métier qui manquaient : validation réellement exécutée, client vérifié, axes normalisés.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordre d'exécution (P3-3B).</b> Valider ⇒ charger le client ⇒ refuser si archivé ⇒ normaliser ⇒ écrire. La
/// validation précède la lecture du client : une commande invalide ne déclenche <b>aucun</b> accès au dépôt.
/// <c>PrescriptionValidator</c> — jusqu'ici du code mort — est enfin exécuté via <see cref="CommandValidation"/> :
/// une commande invalide n'appelle ni <c>CreateAsync</c> ni <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// <b>Client archivé.</b> « Archivé » signifie « plus de <b>nouvelle</b> activité », pas « données gelées » : la
/// création est refusée par une <see cref="BusinessRuleException"/> (<see cref="CustomerArchivedMessage"/>), tandis
/// que la <b>correction</b> d'une ordonnance existante reste permise (<c>UpdatePrescriptionUseCase</c> ne charge même
/// pas le client). Aucune écriture n'a lieu lors du refus.
/// </para>
/// <para>
/// <b>Course résiduelle assumée (multi-poste).</b> Entre la lecture du client et le <c>SaveChangesAsync</c>, un autre
/// poste peut archiver ce client : l'ordonnance passerait alors malgré la garde. Aucun rempart <b>atomique</b> n'est
/// disponible à coût raisonnable — une clé étrangère ne peut pas exprimer « le client ne doit pas être archivé », et
/// SQLite n'offre pas de <c>CHECK</c> inter-tables (un trigger ne serait pas neutre vis-à-vis du fournisseur, cf.
/// ADR-PROD-DB-001). <c>ITransactionRunner</c> n'y changerait rien : le flux reste <b>mono-écriture</b> et une
/// transaction ne sérialise pas l'archivage venu d'une autre connexion. Le risque est donc <b>accepté et documenté</b> :
/// fenêtre étroite, <b>aucune perte de donnée</b>, situation corrigeable (réactiver le client, ou supprimer
/// l'ordonnance indûment créée).
/// </para>
/// <para>
/// <b>Normalisation.</b> Seuls les axes sont mis sous forme canonique (<c>0 → 180</c>,
/// <see cref="OpticalAxisNormalizer"/>) — après la validation, qui accepte l'axe <c>0</c> en saisie. Les champs texte
/// optionnels restent <b>non normalisés</b> (comportement d'origine conservé). <c>CreatedAt</c> reste à la valeur par
/// défaut de l'entité (<see cref="DateTime.UtcNow"/>).
/// </para>
/// </remarks>
public sealed class CreatePrescriptionUseCase : ICreatePrescriptionUseCase
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'une ordonnance est créée pour un client archivé. Exposé en constante pour
    /// que l'UI (P3-3C) et les tests s'y réfèrent sans le dupliquer (patron P3-2B
    /// <c>DeleteCustomerUseCase.CustomerHasHistoryMessage</c>).
    /// </summary>
    public const string CustomerArchivedMessage =
        "Ce client est archivé. Réactivez-le avant de créer une nouvelle ordonnance.";

    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication, cf. ADR frontières §9).
    // Stateless et thread-safe : une seule instance partagée suffit.
    private static readonly PrescriptionValidator PrescriptionValidator = new();

    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePrescriptionUseCase(
        IPrescriptionRepository prescriptionRepository,
        ICustomerRepository customerRepository,
        IUnitOfWork unitOfWork)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<CreatePrescriptionResult> ExecuteAsync(CreatePrescriptionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Candidat non persisté (jamais suivi par le contexte) : c'est lui qui est validé, donc exactement ce qui
        // serait écrit.
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

        var validationErrors = CommandValidation.Validate(PrescriptionValidator, prescription);
        if (validationErrors.Count > 0)
            return new CreatePrescriptionResult { ValidationErrors = validationErrors };

        var customer = await _customerRepository.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer is null)
            return new CreatePrescriptionResult { CustomerFound = false };

        if (customer.IsArchived)
            throw new BusinessRuleException(CustomerArchivedMessage);

        // Valeurs canoniques produites APRÈS la validation (l'axe 0 est accepté en saisie, persisté à 180).
        prescription.OdAxis = OpticalAxisNormalizer.NormalizeAxis(prescription.OdAxis);
        prescription.OgAxis = OpticalAxisNormalizer.NormalizeAxis(prescription.OgAxis);

        await _prescriptionRepository.CreateAsync(prescription, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreatePrescriptionResult
        {
            PrescriptionId = prescription.PrescriptionId,
            CustomerFound = true
        };
    }
}
