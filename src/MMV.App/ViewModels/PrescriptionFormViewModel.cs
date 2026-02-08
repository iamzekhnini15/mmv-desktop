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
        set => SetProperty(ref _odCylinder, value);
    }

    /// <summary>
    /// Axe de l'œil droit (0 à 180°).
    /// </summary>
    public int? OdAxis
    {
        get => _odAxis;
        set => SetProperty(ref _odAxis, value);
    }

    /// <summary>
    /// Addition de l'œil droit (0.00 à +4.00).
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
        set => SetProperty(ref _ogSphere, value);
    }

    /// <summary>
    /// Cylindre de l'œil gauche (-6.00 à +6.00).
    /// </summary>
    public double? OgCylinder
    {
        get => _ogCylinder;
        set => SetProperty(ref _ogCylinder, value);
    }

    /// <summary>
    /// Axe de l'œil gauche (0 à 180°).
    /// </summary>
    public int? OgAxis
    {
        get => _ogAxis;
        set => SetProperty(ref _ogAxis, value);
    }

    /// <summary>
    /// Addition de l'œil gauche (0.00 à +4.00).
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
        if (OdSphere.HasValue && (OdSphere.Value < -20 || OdSphere.Value > 20))
        {
            // Note: Pour l'instant, pas de propriété d'erreur visible
            // À ajouter si nécessaire
        }
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
