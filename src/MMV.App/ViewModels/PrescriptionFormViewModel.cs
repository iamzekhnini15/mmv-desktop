using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.Common;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;

namespace MMV.App.ViewModels;

/// <summary>
/// Option de saisie de la base d'un prisme : couple « valeur métier ⇄ libellé utilisateur ».
/// </summary>
/// <remarks>
/// P3-3C : représentation <b>UI</b> minimale, introduite parce que le dépôt n'a aucune convention réutilisable pour
/// lier un enum <b>nullable</b> à un <c>ComboBox</c> (la seule existante — <c>SaleFormViewModel.PaymentMethodOptions</c>
/// — repose sur un <c>ComboBox</c> de chaînes reconverties en enum par un <c>switch</c> textuel : c'est exactement la
/// « conversion de chaîne fragile » que cette étape doit éliminer). L'enum Domain <see cref="PrismBase"/> n'est
/// <b>pas</b> dupliqué : <see cref="Value"/> le porte tel quel, et seul le <see cref="Label"/> est francisé. La valeur
/// persistée reste donc toujours <c>In</c> / <c>Out</c> / <c>Up</c> / <c>Down</c> ; « aucune base » est représentée par
/// <c>null</c>, jamais par une valeur d'enum inventée.
/// </remarks>
/// <param name="Value">Valeur métier envoyée au use case (<c>null</c> = aucune base).</param>
/// <param name="Label">Libellé affiché à l'utilisateur.</param>
public sealed record PrismBaseOption(PrismBase? Value, string Label);

/// <summary>
/// ViewModel pour le formulaire de création / modification d'une ordonnance.
/// </summary>
/// <remarks>
/// <para>
/// <b>P3-3C.</b> Les règles optiques (plages, valeurs finies, cylindre ⇄ axe, prisme ⇄ base, date) appartiennent au
/// <b>Domain</b> depuis P3-3B, où elles sont réellement exécutées par les use cases via <c>PrescriptionValidator</c>.
/// Cette ViewModel ne les réimplémente donc plus : elle construit la commande, délègue, et <b>affiche</b> les
/// <c>ValidationErrors</c> du résultat. Toute règle recopiée ici serait une seconde source de vérité, condamnée à
/// diverger (c'est précisément ce qui rendait la saisie d'un prisme impossible entre P3-3B et P3-3C).
/// </para>
/// <para>
/// L'axe <c>0</c> est envoyé <b>tel quel</b> : sa normalisation vers <c>180</c> appartient au use case
/// (<c>OpticalAxisNormalizer</c>). La valeur canonique revient par le rechargement du parent depuis la source, jamais
/// par une correction locale anticipée.
/// </para>
/// </remarks>
public class PrescriptionFormViewModel : BaseViewModel
{
    /// <summary>Titre des dialogues d'erreur de sauvegarde (convention du dépôt).</summary>
    public const string SaveErrorTitle = "Enregistrement impossible";

    /// <summary>
    /// Message affiché quand le client visé n'existe plus (<c>CustomerFound = false</c>) : supprimé depuis un autre
    /// poste, ou fiche restée ouverte. Aucune écriture n'a eu lieu.
    /// </summary>
    public const string CustomerNotFoundMessage =
        "Ce client n'existe plus. Actualisez la fiche client avant de réessayer.";

    /// <summary>Message affiché quand l'ordonnance à modifier n'existe plus (<c>PrescriptionFound = false</c>).</summary>
    public const string PrescriptionNotFoundMessage = "L'ordonnance à modifier est introuvable.";

    /// <summary>
    /// Options de saisie de la base du prisme, <b>seule</b> source de valeurs proposées à l'utilisateur : les quatre
    /// valeurs réelles de l'enum Domain, plus l'absence de base (<c>null</c>). Aucune valeur « H » ou « V » n'existe.
    /// </summary>
    public static IReadOnlyList<PrismBaseOption> PrismBaseOptions { get; } = new[]
    {
        new PrismBaseOption(null, "Aucune"),
        new PrismBaseOption(PrismBase.In, "Interne"),
        new PrismBaseOption(PrismBase.Out, "Externe"),
        new PrismBaseOption(PrismBase.Up, "Haut"),
        new PrismBaseOption(PrismBase.Down, "Bas")
    };

    private readonly ICreatePrescriptionUseCase _createPrescriptionUseCase;
    private readonly IUpdatePrescriptionUseCase _updatePrescriptionUseCase;
    private readonly IDialogService _dialogService;

    // 0 = création ; > 0 = édition d'une ordonnance existante (renseigné par LoadPrescription).
    private long _prescriptionId;
    private long _customerId;
    private DateTimeOffset _issueDate = DateTimeOffset.Now;
    private string _doctorName = string.Empty;
    private double? _odSphere;
    private double? _odCylinder;
    private int? _odAxis;
    private double? _odAddition;
    private double? _odPrismValue;
    private PrismBase? _odPrismBase;
    private string? _odVisualAcuity;

    private double? _ogSphere;
    private double? _ogCylinder;
    private int? _ogAxis;
    private double? _ogAddition;
    private double? _ogPrismValue;
    private PrismBase? _ogPrismBase;
    private string? _ogVisualAcuity;
    private string _notes = string.Empty;
    private bool _isSaving;

    #region Propriétés OD (Œil Droit)

    /// <summary>
    /// Sphère de l'œil droit. Plage validée par le Domain (P3-3B).
    /// </summary>
    public double? OdSphere
    {
        get => _odSphere;
        set => SetProperty(ref _odSphere, value);
    }

    /// <summary>
    /// Cylindre de l'œil droit. Plage et cohérence avec l'axe validées par le Domain (P3-3B).
    /// </summary>
    public double? OdCylinder
    {
        get => _odCylinder;
        set => SetProperty(ref _odCylinder, value);
    }

    /// <summary>
    /// Axe de l'œil droit. <c>0</c> est une saisie valide (normalisée vers 180 par le use case).
    /// </summary>
    public int? OdAxis
    {
        get => _odAxis;
        set => SetProperty(ref _odAxis, value);
    }

    /// <summary>
    /// Addition de l'œil droit. Plage validée par le Domain (P3-3B).
    /// </summary>
    public double? OdAddition
    {
        get => _odAddition;
        set => SetProperty(ref _odAddition, value);
    }

    /// <summary>
    /// Valeur du prisme de l'œil droit.
    /// </summary>
    public double? OdPrismValue
    {
        get => _odPrismValue;
        set => SetProperty(ref _odPrismValue, value);
    }

    /// <summary>
    /// Base du prisme de l'œil droit (valeur métier envoyée au use case).
    /// </summary>
    public PrismBase? OdPrismBase
    {
        get => _odPrismBase;
        set
        {
            if (SetProperty(ref _odPrismBase, value))
            {
                OnPropertyChanged(nameof(SelectedOdPrismBaseOption));
            }
        }
    }

    /// <summary>
    /// Option de base du prisme OD sélectionnée dans le <c>ComboBox</c> (projection de <see cref="OdPrismBase"/>).
    /// </summary>
    public PrismBaseOption? SelectedOdPrismBaseOption
    {
        get => OptionFor(_odPrismBase);
        set => OdPrismBase = value?.Value;
    }

    /// <summary>
    /// Acuité visuelle de l'œil droit.
    /// </summary>
    public string? OdVisualAcuity
    {
        get => _odVisualAcuity;
        set => SetProperty(ref _odVisualAcuity, value);
    }

    #endregion

    #region Propriétés OG (Œil Gauche)

    /// <summary>
    /// Sphère de l'œil gauche. Plage validée par le Domain (P3-3B).
    /// </summary>
    public double? OgSphere
    {
        get => _ogSphere;
        set => SetProperty(ref _ogSphere, value);
    }

    /// <summary>
    /// Cylindre de l'œil gauche. Plage et cohérence avec l'axe validées par le Domain (P3-3B).
    /// </summary>
    public double? OgCylinder
    {
        get => _ogCylinder;
        set => SetProperty(ref _ogCylinder, value);
    }

    /// <summary>
    /// Axe de l'œil gauche. <c>0</c> est une saisie valide (normalisée vers 180 par le use case).
    /// </summary>
    public int? OgAxis
    {
        get => _ogAxis;
        set => SetProperty(ref _ogAxis, value);
    }

    /// <summary>
    /// Addition de l'œil gauche. Plage validée par le Domain (P3-3B).
    /// </summary>
    public double? OgAddition
    {
        get => _ogAddition;
        set => SetProperty(ref _ogAddition, value);
    }

    /// <summary>
    /// Valeur du prisme de l'œil gauche.
    /// </summary>
    public double? OgPrismValue
    {
        get => _ogPrismValue;
        set => SetProperty(ref _ogPrismValue, value);
    }

    /// <summary>
    /// Base du prisme de l'œil gauche (valeur métier envoyée au use case).
    /// </summary>
    public PrismBase? OgPrismBase
    {
        get => _ogPrismBase;
        set
        {
            if (SetProperty(ref _ogPrismBase, value))
            {
                OnPropertyChanged(nameof(SelectedOgPrismBaseOption));
            }
        }
    }

    /// <summary>
    /// Option de base du prisme OG sélectionnée dans le <c>ComboBox</c> (projection de <see cref="OgPrismBase"/>).
    /// </summary>
    public PrismBaseOption? SelectedOgPrismBaseOption
    {
        get => OptionFor(_ogPrismBase);
        set => OgPrismBase = value?.Value;
    }

    /// <summary>
    /// Acuité visuelle de l'œil gauche.
    /// </summary>
    public string? OgVisualAcuity
    {
        get => _ogVisualAcuity;
        set => SetProperty(ref _ogVisualAcuity, value);
    }

    #endregion

    #region Propriétés Générales

    /// <summary>
    /// ID du client pour cette ordonnance.
    /// </summary>
    public long CustomerId
    {
        get => _customerId;
        set => SetProperty(ref _customerId, value);
    }

    /// <summary>
    /// Date d'émission de l'ordonnance.
    /// </summary>
    public DateTimeOffset IssueDate
    {
        get => _issueDate;
        set => SetProperty(ref _issueDate, value);
    }

    /// <summary>
    /// Nom du médecin prescripteur. <b>Facultatif</b> (P3-3B) : null, vide ou blanc n'empêche pas l'enregistrement.
    /// </summary>
    public string DoctorName
    {
        get => _doctorName;
        set => SetProperty(ref _doctorName, value);
    }

    /// <summary>
    /// Notes supplémentaires.
    /// </summary>
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>
    /// Indique si l'ordonnance est en cours de sauvegarde.
    /// </summary>
    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    /// <summary>
    /// Erreurs de validation renvoyées par le use case (P3-1 / P3-3B), dans l'ordre, sans doublon. Vide tant
    /// qu'aucune commande n'a été refusée.
    /// </summary>
    public ObservableCollection<string> ValidationMessages { get; } = new();

    /// <summary>
    /// Indique si le résumé de validation doit être affiché.
    /// </summary>
    public bool HasValidationErrors => ValidationMessages.Count > 0;

    #endregion

    /// <summary>
    /// Commande pour sauvegarder l'ordonnance.
    /// </summary>
    public ICommand SaveCommand { get; }

    /// <summary>
    /// Commande pour annuler.
    /// </summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Événement déclenché quand l'ordonnance est sauvegardée.
    /// </summary>
    public event EventHandler<Prescription>? PrescriptionSaved;

    /// <summary>
    /// Événement déclenché quand on annule.
    /// </summary>
    public event EventHandler? Cancelled;

    public PrescriptionFormViewModel(
        ICreatePrescriptionUseCase createPrescriptionUseCase,
        IUpdatePrescriptionUseCase updatePrescriptionUseCase,
        IDialogService dialogService)
    {
        _createPrescriptionUseCase = createPrescriptionUseCase ?? throw new ArgumentNullException(nameof(createPrescriptionUseCase));
        _updatePrescriptionUseCase = updatePrescriptionUseCase ?? throw new ArgumentNullException(nameof(updatePrescriptionUseCase));
        // P3-3C : le refus métier « client archivé » doit être incontournable — il passe donc aussi par le mécanisme
        // de dialogue existant du dépôt, en plus du message inline.
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        SaveCommand = new RelayCommand(async () => await SavePrescriptionAsync());
        CancelCommand = new RelayCommand(ExecuteCancel);

        Title = "Nouvelle Ordonnance";
    }

    /// <summary>
    /// Option correspondant à une valeur métier. Renvoie l'instance présente dans <see cref="PrismBaseOptions"/> afin
    /// que le <c>ComboBox</c> retrouve sa sélection.
    /// </summary>
    private static PrismBaseOption OptionFor(PrismBase? value) =>
        PrismBaseOptions.First(option => option.Value == value);

    private void ExecuteCancel()
    {
        ClearForm();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void ClearForm()
    {
        _prescriptionId = 0;
        IssueDate = DateTimeOffset.Now;
        DoctorName = string.Empty;
        OdSphere = null;
        OdCylinder = null;
        OdAxis = null;
        OdAddition = null;
        OdPrismValue = null;
        OdPrismBase = null;
        OdVisualAcuity = null;
        OgSphere = null;
        OgCylinder = null;
        OgAxis = null;
        OgAddition = null;
        OgPrismValue = null;
        OgPrismBase = null;
        OgVisualAcuity = null;
        Notes = string.Empty;
        ClearMessages();
    }

    /// <summary>
    /// Charge les données d'une ordonnance existante pour modification.
    /// </summary>
    // P2D-5 : le pré-remplissage consomme un DTO applicatif plat (PrescriptionListItemDto) au lieu de l'entité EF
    // Prescription. Simple recopie de champs identiques ; le flux de sauvegarde (reconstruction d'un snapshot à partir
    // des propriétés du formulaire) reste inchangé.
    public void LoadPrescription(PrescriptionListItemDto prescription)
    {
        _prescriptionId = prescription.PrescriptionId;
        CustomerId = prescription.CustomerId;
        IssueDate = new DateTimeOffset(prescription.IssueDate);
        DoctorName = prescription.DoctorName ?? string.Empty;

        OdSphere = prescription.OdSphere;
        OdCylinder = prescription.OdCylinder;
        OdAxis = prescription.OdAxis;
        OdAddition = prescription.OdAddition;
        OdPrismValue = prescription.OdPrismValue;
        OdPrismBase = prescription.OdPrismBase;
        OdVisualAcuity = prescription.OdVisualAcuity;

        OgSphere = prescription.OgSphere;
        OgCylinder = prescription.OgCylinder;
        OgAxis = prescription.OgAxis;
        OgAddition = prescription.OgAddition;
        OgPrismValue = prescription.OgPrismValue;
        OgPrismBase = prescription.OgPrismBase;
        OgVisualAcuity = prescription.OgVisualAcuity;

        Notes = prescription.Notes ?? string.Empty;

        Title = "Modifier Ordonnance";
    }

    /// <summary>
    /// Efface les messages affichés (résumé de validation et message d'erreur), <b>sans</b> toucher aux valeurs saisies.
    /// </summary>
    private void ClearMessages()
    {
        ValidationMessages.Clear();
        OnPropertyChanged(nameof(HasValidationErrors));
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Affiche les erreurs de validation d'un résultat de use case : ordre conservé, doublons retirés, une erreur par
    /// ligne. Le formulaire reste ouvert et les valeurs saisies sont conservées.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Les messages proviennent des validateurs Domain (P3-3B) : ils sont déjà destinés à l'utilisateur. On les affiche
    /// tels quels — jamais un nom d'exception, jamais une pile — et on ne les <b>compare</b> jamais pour en déduire un
    /// comportement métier, ce qui serait fragile.
    /// </para>
    /// <para>
    /// <see cref="BaseViewModel.ErrorMessage"/> n'est <b>pas</b> renseigné ici : il reste réservé aux messages
    /// <b>uniques</b> (client introuvable, ordonnance introuvable, refus métier, erreur inattendue). Les deux canaux
    /// sont donc disjoints, et la vue n'affiche jamais deux fois la même information.
    /// </para>
    /// </remarks>
    private void ShowValidationErrors(IReadOnlyList<ValidationError> errors)
    {
        ValidationMessages.Clear();
        foreach (var message in errors.Select(error => error.Message).Distinct())
        {
            ValidationMessages.Add(message);
        }

        OnPropertyChanged(nameof(HasValidationErrors));
    }

    /// <summary>
    /// Sauvegarde l'ordonnance via la couche Application : création (<see cref="ICreatePrescriptionUseCase"/>) si le
    /// formulaire est vierge, ou mise à jour (<see cref="IUpdatePrescriptionUseCase"/>) si une ordonnance existante a
    /// été chargée (<c>_prescriptionId &gt; 0</c>).
    /// </summary>
    /// <remarks>
    /// Aucune validation métier locale : la commande part telle qu'elle a été saisie, et c'est le use case qui décide.
    /// Sur tout refus (validation, client introuvable, ordonnance introuvable, client archivé), le formulaire n'est ni
    /// fermé ni vidé et <see cref="PrescriptionSaved"/> n'est pas émis.
    /// </remarks>
    private async Task SavePrescriptionAsync()
    {
        IsSaving = true;
        ClearMessages();

        try
        {
            var prescriptionId = _prescriptionId > 0
                ? await UpdateExistingAsync()
                : await CreateNewAsync();

            // Refus : le use case a déjà renseigné le message ou le résumé de validation.
            if (prescriptionId is null)
            {
                return;
            }

            // Entité reconstruite pour la charge utile de l'événement PrescriptionSaved (le parent l'écoute pour
            // recharger la liste depuis la source). Elle n'est pas la vérité : elle ne porte pas la normalisation
            // d'axe appliquée par le use case.
            var prescription = BuildPrescriptionSnapshot();
            prescription.PrescriptionId = prescriptionId.Value;

            PrescriptionSaved?.Invoke(this, prescription);
            ClearForm();
        }
        catch (BusinessRuleException ex)
        {
            // Refus métier dur (client archivé, P3-3B) : on affiche le message porté par l'exception, qui est déjà
            // celui destiné à l'utilisateur (CreatePrescriptionUseCase.CustomerArchivedMessage) et indique l'action à
            // mener — réactiver le client. P3-3C ne l'exécute pas : réactiver depuis ici serait une écriture client
            // décidée par l'écran des ordonnances.
            ErrorMessage = ex.Message;
            await _dialogService.ShowErrorAsync(SaveErrorTitle, ex.Message);
        }
        catch (Exception ex)
        {
            // Erreurs inattendues (techniques) : traitement générique, distinct du refus métier ci-dessus.
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Exécute la création. Renvoie l'identifiant créé, ou <c>null</c> si la commande a été refusée.
    /// </summary>
    private async Task<long?> CreateNewAsync()
    {
        var result = await _createPrescriptionUseCase.ExecuteAsync(new CreatePrescriptionCommand
        {
            CustomerId = CustomerId,
            IssueDate = IssueDate.UtcDateTime,
            DoctorName = DoctorName,
            OdSphere = OdSphere,
            OdCylinder = OdCylinder,
            OdAxis = OdAxis,
            OdAddition = OdAddition,
            OdPrismValue = OdPrismValue,
            OdPrismBase = OdPrismBase,
            OdVisualAcuity = OdVisualAcuity,
            OgSphere = OgSphere,
            OgCylinder = OgCylinder,
            OgAxis = OgAxis,
            OgAddition = OgAddition,
            OgPrismValue = OgPrismValue,
            OgPrismBase = OgPrismBase,
            OgVisualAcuity = OgVisualAcuity,
            Notes = Notes
        });

        if (!result.IsValid)
        {
            ShowValidationErrors(result.ValidationErrors);
            return null;
        }

        if (!result.CustomerFound)
        {
            ErrorMessage = CustomerNotFoundMessage;
            return null;
        }

        return result.PrescriptionId;
    }

    /// <summary>
    /// Exécute la mise à jour. Renvoie l'identifiant modifié, ou <c>null</c> si la commande a été refusée.
    /// </summary>
    /// <remarks>
    /// Aucun blocage local sur l'archivage du client : la <b>correction</b> d'une ordonnance existante reste autorisée
    /// pour un client archivé (P3-3B §15).
    /// </remarks>
    private async Task<long?> UpdateExistingAsync()
    {
        var result = await _updatePrescriptionUseCase.ExecuteAsync(new UpdatePrescriptionCommand
        {
            PrescriptionId = _prescriptionId,
            IssueDate = IssueDate.UtcDateTime,
            DoctorName = DoctorName,
            OdSphere = OdSphere,
            OdCylinder = OdCylinder,
            OdAxis = OdAxis,
            OdAddition = OdAddition,
            OdPrismValue = OdPrismValue,
            OdPrismBase = OdPrismBase,
            OdVisualAcuity = OdVisualAcuity,
            OgSphere = OgSphere,
            OgCylinder = OgCylinder,
            OgAxis = OgAxis,
            OgAddition = OgAddition,
            OgPrismValue = OgPrismValue,
            OgPrismBase = OgPrismBase,
            OgVisualAcuity = OgVisualAcuity,
            Notes = Notes
        });

        if (!result.PrescriptionFound)
        {
            ErrorMessage = PrescriptionNotFoundMessage;
            return null;
        }

        if (!result.IsValid)
        {
            ShowValidationErrors(result.ValidationErrors);
            return null;
        }

        return _prescriptionId;
    }

    /// <summary>
    /// Construit une <see cref="Prescription"/> reflétant l'état courant du formulaire (charge utile de l'événement
    /// <see cref="PrescriptionSaved"/>). N'effectue aucune persistance.
    /// </summary>
    private Prescription BuildPrescriptionSnapshot() => new()
    {
        CustomerId = CustomerId,
        IssueDate = IssueDate.UtcDateTime,
        DoctorName = DoctorName,
        OdSphere = OdSphere,
        OdCylinder = OdCylinder,
        OdAxis = OdAxis,
        OdAddition = OdAddition,
        OdPrismValue = OdPrismValue,
        OdPrismBase = OdPrismBase,
        OdVisualAcuity = OdVisualAcuity,
        OgSphere = OgSphere,
        OgCylinder = OgCylinder,
        OgAxis = OgAxis,
        OgAddition = OgAddition,
        OgPrismValue = OgPrismValue,
        OgPrismBase = OgPrismBase,
        OgVisualAcuity = OgVisualAcuity,
        Notes = Notes
    };
}
