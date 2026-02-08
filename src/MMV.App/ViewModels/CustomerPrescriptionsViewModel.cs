using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour gérer les ordonnances d'un client.
/// </summary>
public class CustomerPrescriptionsViewModel : BaseViewModel
{
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private long _customerId;
    private bool _isInEditMode;
    private bool _isShowingDetail;
    private ObservableCollection<Prescription> _prescriptions;
    private Prescription? _selectedPrescription;
    private PrescriptionFormViewModel? _formViewModel;
    private PrescriptionDetailViewModel? _detailViewModel;

    /// <summary>
    /// ID du client.
    /// </summary>
    public long CustomerId
    {
        get => _customerId;
        set => SetProperty(ref _customerId, value);
    }

    /// <summary>
    /// Liste des ordonnances du client.
    /// </summary>
    public ObservableCollection<Prescription> Prescriptions
    {
        get => _prescriptions;
        set => SetProperty(ref _prescriptions, value);
    }

    /// <summary>
    /// Ordonnance sélectionnée.
    /// </summary>
    public Prescription? SelectedPrescription
    {
        get => _selectedPrescription;
        set => SetProperty(ref _selectedPrescription, value);
    }

    /// <summary>
    /// Indique si le formulaire d'édition est affiché.
    /// </summary>
    public bool IsInEditMode
    {
        get => _isInEditMode;
        set
        {
            if (SetProperty(ref _isInEditMode, value))
            {
                OnPropertyChanged(nameof(ShowList));
            }
        }
    }

    /// <summary>
    /// Indique si les détails sont affichés.
    /// </summary>
    public bool IsShowingDetail
    {
        get => _isShowingDetail;
        set
        {
            if (SetProperty(ref _isShowingDetail, value))
            {
                OnPropertyChanged(nameof(ShowList));
            }
        }
    }

    /// <summary>
    /// Indique si la liste doit être affichée (ni formulaire ni détails).
    /// </summary>
    public bool ShowList => !IsInEditMode && !IsShowingDetail;

    /// <summary>
    /// Indique s'il y a des ordonnances.
    /// </summary>
    public bool HasPrescriptions => Prescriptions.Any();

    /// <summary>
    /// Indique s'il n'y a pas d'ordonnances.
    /// </summary>
    public bool HasNoPrescriptions => !HasPrescriptions;

    /// <summary>
    /// ViewModel du formulaire d'ordonnance.
    /// </summary>
    public PrescriptionFormViewModel? FormViewModel
    {
        get => _formViewModel;
        set => SetProperty(ref _formViewModel, value);
    }

    /// <summary>
    /// ViewModel des détails de l'ordonnance.
    /// </summary>
    public PrescriptionDetailViewModel? DetailViewModel
    {
        get => _detailViewModel;
        set => SetProperty(ref _detailViewModel, value);
    }

    /// <summary>
    /// Commande pour créer une nouvelle ordonnance.
    /// </summary>
    public ICommand CreateCommand { get; }

    /// <summary>
    /// Commande pour voir les détails d'une ordonnance.
    /// </summary>
    public ICommand ViewDetailsCommand { get; }

    /// <summary>
    /// Commande pour modifier une ordonnance.
    /// </summary>
    public ICommand EditCommand { get; }

    /// <summary>
    /// Commande pour supprimer une ordonnance.
    /// </summary>
    public ICommand DeleteCommand { get; }

    /// <summary>
    /// Commande pour actualiser la liste.
    /// </summary>
    public ICommand RefreshCommand { get; }

    public CustomerPrescriptionsViewModel(IPrescriptionRepository prescriptionRepository, IUnitOfWork unitOfWork)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _prescriptions = new ObservableCollection<Prescription>();

        CreateCommand = new RelayCommand(ExecuteCreate);
        ViewDetailsCommand = new RelayCommand<Prescription>(ExecuteViewDetails);
        EditCommand = new RelayCommand<Prescription>(ExecuteEdit);
        DeleteCommand = new RelayCommand<Prescription>(async (p) => await ExecuteDeleteAsync(p));
        RefreshCommand = new RelayCommand(async () => await LoadPrescriptionsAsync());

        Title = "Ordonnances";
    }

    /// <summary>
    /// Initialise le ViewModel avec un client.
    /// </summary>
    public async Task InitializeAsync(long customerId)
    {
        CustomerId = customerId;
        await LoadPrescriptionsAsync();
    }

    /// <summary>
    /// Charge les ordonnances du client.
    /// </summary>
    private async Task LoadPrescriptionsAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var prescriptions = await _prescriptionRepository.GetByCustomerIdAsync(CustomerId);
            
            Prescriptions.Clear();
            foreach (var prescription in prescriptions.OrderByDescending(p => p.IssueDate))
            {
                Prescriptions.Add(prescription);
            }

            OnPropertyChanged(nameof(HasPrescriptions));
            OnPropertyChanged(nameof(HasNoPrescriptions));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des ordonnances : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Crée une nouvelle ordonnance.
    /// </summary>
    private void ExecuteCreate()
    {
        FormViewModel = new PrescriptionFormViewModel(_prescriptionRepository, _unitOfWork)
        {
            CustomerId = CustomerId
        };
        FormViewModel.PrescriptionSaved += OnPrescriptionSaved;
        FormViewModel.Cancelled += OnFormCancelled;

        IsInEditMode = true;
    }

    /// <summary>
    /// Affiche les détails d'une ordonnance.
    /// </summary>
    private void ExecuteViewDetails(Prescription? prescription)
    {
        if (prescription == null) return;

        DetailViewModel = new PrescriptionDetailViewModel(_prescriptionRepository)
        {
            CurrentPrescription = prescription
        };
        DetailViewModel.EditRequested += OnEditRequested;
        DetailViewModel.DeleteRequested += OnDeleteRequested;
        DetailViewModel.BackRequested += OnDetailBackRequested;

        IsShowingDetail = true;
    }

    /// <summary>
    /// Modifie une ordonnance existante.
    /// </summary>
    private void ExecuteEdit(Prescription? prescription)
    {
        if (prescription == null) return;

        FormViewModel = new PrescriptionFormViewModel(_prescriptionRepository, _unitOfWork);
        FormViewModel.LoadPrescription(prescription);
        FormViewModel.PrescriptionSaved += OnPrescriptionSaved;
        FormViewModel.Cancelled += OnFormCancelled;

        IsInEditMode = true;
    }

    /// <summary>
    /// Supprime une ordonnance.
    /// </summary>
    private async Task ExecuteDeleteAsync(Prescription? prescription)
    {
        if (prescription == null) return;

        // TODO: Ajouter confirmation
        try
        {
            await _prescriptionRepository.DeleteAsync(prescription.PrescriptionId);
            await _unitOfWork.CommitAsync();
            await LoadPrescriptionsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la suppression : {ex.Message}";
        }
    }

    private async void OnPrescriptionSaved(object? sender, Prescription prescription)
    {
        IsInEditMode = false;
        FormViewModel = null;
        await LoadPrescriptionsAsync();
    }

    private void OnFormCancelled(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        FormViewModel = null;
    }

    private void OnEditRequested(object? sender, Prescription prescription)
    {
        IsShowingDetail = false;
        ExecuteEdit(prescription);
    }

    private async void OnDeleteRequested(object? sender, Prescription prescription)
    {
        IsShowingDetail = false;
        DetailViewModel = null;
        await ExecuteDeleteAsync(prescription);
    }

    private void OnDetailBackRequested(object? sender, EventArgs e)
    {
        IsShowingDetail = false;
        DetailViewModel = null;
    }
}
