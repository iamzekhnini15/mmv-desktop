using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création d'une ordonnance.
/// </summary>
public class PrescriptionFormViewModel : BaseViewModel
{
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IUnitOfWork _unitOfWork;

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

    // Propriétés d'erreur pour la validation
    private string _odSphereError = string.Empty;
    private string _odCylinderError = string.Empty;
    private string _odAxisError = string.Empty;
    private string _odAdditionError = string.Empty;
    private string _ogSphereError = string.Empty;
    private string _ogCylinderError = string.Empty;
    private string _ogAxisError = string.Empty;
    private string _ogAdditionError = string.Empty;
    private string _doctorNameError = string.Empty;

    #region Propriétés OD (Œil Droit)

    /// <summary>
    /// Sphère de l'œil droit (-20.00 à +20.00).
    /// </summary>
    public double? OdSphere
    {
        get => _odSphere;
        set
        {
            if (SetProperty(ref _odSphere, value))
            {
                ValidateOdSphere();
            }
        }
    }

    /// <summary>
    /// Cylindre de l'œil droit (-6.00 à +6.00).
    /// </summary>
    public double? OdCylinder
    {
        get => _odCylinder;
        set
        {
            if (SetProperty(ref _odCylinder, value))
            {
                ValidateOdCylinder();
            }
        }
    }

    /// <summary>
    /// Axe de l'œil droit (0 à 180°).
    /// </summary>
    public int? OdAxis
    {
        get => _odAxis;
        set
        {
            if (SetProperty(ref _odAxis, value))
            {
                ValidateOdAxis();
            }
        }
    }

    /// <summary>
    /// Addition de l'œil droit (0.00 à +4.00).
    /// </summary>
    public double? OdAddition
    {
        get => _odAddition;
        set
        {
            if (SetProperty(ref _odAddition, value))
            {
                ValidateOdAddition();
            }
        }
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
    /// Base du prisme de l'œil droit.
    /// </summary>
    public PrismBase? OdPrismBase
    {
        get => _odPrismBase;
        set => SetProperty(ref _odPrismBase, value);
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
    /// Sphère de l'œil gauche (-20.00 à +20.00).
    /// </summary>
    public double? OgSphere
    {
        get => _ogSphere;
        set
        {
            if (SetProperty(ref _ogSphere, value))
            {
                ValidateOgSphere();
            }
        }
    }

    /// <summary>
    /// Cylindre de l'œil gauche (-6.00 à +6.00).
    /// </summary>
    public double? OgCylinder
    {
        get => _ogCylinder;
        set
        {
            if (SetProperty(ref _ogCylinder, value))
            {
                ValidateOgCylinder();
            }
        }
    }

    /// <summary>
    /// Axe de l'œil gauche (0 à 180°).
    /// </summary>
    public int? OgAxis
    {
        get => _ogAxis;
        set
        {
            if (SetProperty(ref _ogAxis, value))
            {
                ValidateOgAxis();
            }
        }
    }

    /// <summary>
    /// Addition de l'œil gauche (0.00 à +4.00).
    /// </summary>
    public double? OgAddition
    {
        get => _ogAddition;
        set
        {
            if (SetProperty(ref _ogAddition, value))
            {
                ValidateOgAddition();
            }
        }
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
    /// Base du prisme de l'œil gauche.
    /// </summary>
    public PrismBase? OgPrismBase
    {
        get => _ogPrismBase;
        set => SetProperty(ref _ogPrismBase, value);
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
    /// Nom du médecin prescripteur.
    /// </summary>
    public string DoctorName
    {
        get => _doctorName;
        set
        {
            if (SetProperty(ref _doctorName, value))
            {
                ValidateDoctorName();
            }
        }
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

    #endregion

    #region Propriétés d'erreur pour la validation

    /// <summary>
    /// Message d'erreur pour la sphère de l'œil droit.
    /// </summary>
    public string OdSphereError
    {
        get => _odSphereError;
        set => SetProperty(ref _odSphereError, value);
    }

    /// <summary>
    /// Message d'erreur pour le cylindre de l'œil droit.
    /// </summary>
    public string OdCylinderError
    {
        get => _odCylinderError;
        set => SetProperty(ref _odCylinderError, value);
    }

    /// <summary>
    /// Message d'erreur pour l'axe de l'œil droit.
    /// </summary>
    public string OdAxisError
    {
        get => _odAxisError;
        set => SetProperty(ref _odAxisError, value);
    }

    /// <summary>
    /// Message d'erreur pour l'addition de l'œil droit.
    /// </summary>
    public string OdAdditionError
    {
        get => _odAdditionError;
        set => SetProperty(ref _odAdditionError, value);
    }

    /// <summary>
    /// Message d'erreur pour la sphère de l'œil gauche.
    /// </summary>
    public string OgSphereError
    {
        get => _ogSphereError;
        set => SetProperty(ref _ogSphereError, value);
    }

    /// <summary>
    /// Message d'erreur pour le cylindre de l'œil gauche.
    /// </summary>
    public string OgCylinderError
    {
        get => _ogCylinderError;
        set => SetProperty(ref _ogCylinderError, value);
    }

    /// <summary>
    /// Message d'erreur pour l'axe de l'œil gauche.
    /// </summary>
    public string OgAxisError
    {
        get => _ogAxisError;
        set => SetProperty(ref _ogAxisError, value);
    }

    /// <summary>
    /// Message d'erreur pour l'addition de l'œil gauche.
    /// </summary>
    public string OgAdditionError
    {
        get => _ogAdditionError;
        set => SetProperty(ref _ogAdditionError, value);
    }

    /// <summary>
    /// Message d'erreur pour le nom du médecin.
    /// </summary>
    public string DoctorNameError
    {
        get => _doctorNameError;
        set => SetProperty(ref _doctorNameError, value);
    }

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

    public PrescriptionFormViewModel(IPrescriptionRepository prescriptionRepository, IUnitOfWork unitOfWork)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

        SaveCommand = new RelayCommand(async () => await SavePrescriptionAsync());
        CancelCommand = new RelayCommand(ExecuteCancel);

        Title = "Nouvelle Ordonnance";
    }

    private void ExecuteCancel()
    {
        ClearForm();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void ClearForm()
    {
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
        ClearErrors();
    }

    /// <summary>
    /// Charge les données d'une ordonnance existante pour modification.
    /// </summary>
    public void LoadPrescription(Prescription prescription)
    {
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
    /// Valide la sphère de l'œil droit.
    /// </summary>
    private void ValidateOdSphere()
    {
        if (!OdSphere.HasValue)
        {
            OdSphereError = string.Empty;
            return;
        }

        if (OdSphere.Value < -20 || OdSphere.Value > 20)
        {
            OdSphereError = "Doit être entre -20.00 et +20.00";
        }
        else
        {
            OdSphereError = string.Empty;
        }
    }

    /// <summary>
    /// Valide le cylindre de l'œil droit.
    /// </summary>
    private void ValidateOdCylinder()
    {
        if (!OdCylinder.HasValue)
        {
            OdCylinderError = string.Empty;
            return;
        }

        if (OdCylinder.Value < -6 || OdCylinder.Value > 6)
        {
            OdCylinderError = "Doit être entre -6.00 et +6.00";
        }
        else
        {
            OdCylinderError = string.Empty;
        }
    }

    /// <summary>
    /// Valide l'axe de l'œil droit.
    /// </summary>
    private void ValidateOdAxis()
    {
        if (!OdAxis.HasValue)
        {
            OdAxisError = string.Empty;
            return;
        }

        if (OdAxis.Value < 0 || OdAxis.Value > 180)
        {
            OdAxisError = "Doit être entre 0° et 180°";
        }
        else
        {
            OdAxisError = string.Empty;
        }
    }

    /// <summary>
    /// Valide l'addition de l'œil droit.
    /// </summary>
    private void ValidateOdAddition()
    {
        if (!OdAddition.HasValue)
        {
            OdAdditionError = string.Empty;
            return;
        }

        if (OdAddition.Value < 0 || OdAddition.Value > 4)
        {
            OdAdditionError = "Doit être entre 0.00 et +4.00";
        }
        else
        {
            OdAdditionError = string.Empty;
        }
    }

    /// <summary>
    /// Valide la sphère de l'œil gauche.
    /// </summary>
    private void ValidateOgSphere()
    {
        if (!OgSphere.HasValue)
        {
            OgSphereError = string.Empty;
            return;
        }

        if (OgSphere.Value < -20 || OgSphere.Value > 20)
        {
            OgSphereError = "Doit être entre -20.00 et +20.00";
        }
        else
        {
            OgSphereError = string.Empty;
        }
    }

    /// <summary>
    /// Valide le cylindre de l'œil gauche.
    /// </summary>
    private void ValidateOgCylinder()
    {
        if (!OgCylinder.HasValue)
        {
            OgCylinderError = string.Empty;
            return;
        }

        if (OgCylinder.Value < -6 || OgCylinder.Value > 6)
        {
            OgCylinderError = "Doit être entre -6.00 et +6.00";
        }
        else
        {
            OgCylinderError = string.Empty;
        }
    }

    /// <summary>
    /// Valide l'axe de l'œil gauche.
    /// </summary>
    private void ValidateOgAxis()
    {
        if (!OgAxis.HasValue)
        {
            OgAxisError = string.Empty;
            return;
        }

        if (OgAxis.Value < 0 || OgAxis.Value > 180)
        {
            OgAxisError = "Doit être entre 0° et 180°";
        }
        else
        {
            OgAxisError = string.Empty;
        }
    }

    /// <summary>
    /// Valide l'addition de l'œil gauche.
    /// </summary>
    private void ValidateOgAddition()
    {
        if (!OgAddition.HasValue)
        {
            OgAdditionError = string.Empty;
            return;
        }

        if (OgAddition.Value < 0 || OgAddition.Value > 4)
        {
            OgAdditionError = "Doit être entre 0.00 et +4.00";
        }
        else
        {
            OgAdditionError = string.Empty;
        }
    }

    /// <summary>
    /// Valide le nom du médecin.
    /// </summary>
    private void ValidateDoctorName()
    {
        if (string.IsNullOrWhiteSpace(DoctorName))
        {
            DoctorNameError = "Le nom du médecin est requis";
        }
        else
        {
            DoctorNameError = string.Empty;
        }
    }

    /// <summary>
    /// Efface toutes les erreurs de validation.
    /// </summary>
    private void ClearErrors()
    {
        OdSphereError = string.Empty;
        OdCylinderError = string.Empty;
        OdAxisError = string.Empty;
        OdAdditionError = string.Empty;
        OgSphereError = string.Empty;
        OgCylinderError = string.Empty;
        OgAxisError = string.Empty;
        OgAdditionError = string.Empty;
        DoctorNameError = string.Empty;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Valide tout le formulaire.
    /// </summary>
    /// <returns>True si le formulaire est valide, sinon False.</returns>
    private bool ValidateForm()
    {
        ValidateOdSphere();
        ValidateOdCylinder();
        ValidateOdAxis();
        ValidateOdAddition();
        ValidateOgSphere();
        ValidateOgCylinder();
        ValidateOgAxis();
        ValidateOgAddition();
        ValidateDoctorName();

        return string.IsNullOrEmpty(OdSphereError) &&
               string.IsNullOrEmpty(OdCylinderError) &&
               string.IsNullOrEmpty(OdAxisError) &&
               string.IsNullOrEmpty(OdAdditionError) &&
               string.IsNullOrEmpty(OgSphereError) &&
               string.IsNullOrEmpty(OgCylinderError) &&
               string.IsNullOrEmpty(OgAxisError) &&
               string.IsNullOrEmpty(OgAdditionError) &&
               string.IsNullOrEmpty(DoctorNameError);
    }

    /// <summary>
    /// Sauvegarde l'ordonnance dans la base de données.
    /// </summary>
    private async Task SavePrescriptionAsync()
    {
        if (_prescriptionRepository == null || _unitOfWork == null)
        {
            ErrorMessage = "Impossible de sauvegarder : repository non initialisé";
            return;
        }

        // Validation du formulaire
        if (!ValidateForm())
        {
            ErrorMessage = "Veuillez corriger les erreurs de saisie avant d'enregistrer.";
            return;
        }

        IsSaving = true;
        ErrorMessage = string.Empty;

        try
        {
            var prescription = new Prescription
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

            await _prescriptionRepository.CreateAsync(prescription);
            await _unitOfWork.CommitAsync();

            PrescriptionSaved?.Invoke(this, prescription);
            ClearForm();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }
}
