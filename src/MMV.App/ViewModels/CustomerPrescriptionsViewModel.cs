using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour gérer les ordonnances d'un client.
/// </summary>
public class CustomerPrescriptionsViewModel : BaseViewModel
{
    // P2D-5 : la lecture d'affichage (liste des ordonnances du client) passe désormais par le query use case
    // Application, qui renvoie des DTO plats (PrescriptionListItemDto). Les écritures (create/update/delete) restent
    // déléguées aux use cases Application ci-dessous (P2C-4).
    private readonly IListPrescriptionsByCustomerUseCase _listPrescriptionsUseCase;
    private readonly ICreatePrescriptionUseCase _createPrescriptionUseCase;
    private readonly IUpdatePrescriptionUseCase _updatePrescriptionUseCase;
    private readonly IDeletePrescriptionUseCase _deletePrescriptionUseCase;
    private long _customerId;
    private bool _isInEditMode;
    private bool _isShowingDetail;
    private ObservableCollection<PrescriptionListItemDto> _prescriptions;
    private PrescriptionListItemDto? _selectedPrescription;
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
    public ObservableCollection<PrescriptionListItemDto> Prescriptions
    {
        get => _prescriptions;
        set => SetProperty(ref _prescriptions, value);
    }

    /// <summary>
    /// Ordonnance sélectionnée.
    /// </summary>
    public PrescriptionListItemDto? SelectedPrescription
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

    public CustomerPrescriptionsViewModel(
        IListPrescriptionsByCustomerUseCase listPrescriptionsUseCase,
        ICreatePrescriptionUseCase createPrescriptionUseCase,
        IUpdatePrescriptionUseCase updatePrescriptionUseCase,
        IDeletePrescriptionUseCase deletePrescriptionUseCase)
    {
        _listPrescriptionsUseCase = listPrescriptionsUseCase ?? throw new ArgumentNullException(nameof(listPrescriptionsUseCase));
        _createPrescriptionUseCase = createPrescriptionUseCase ?? throw new ArgumentNullException(nameof(createPrescriptionUseCase));
        _updatePrescriptionUseCase = updatePrescriptionUseCase ?? throw new ArgumentNullException(nameof(updatePrescriptionUseCase));
        _deletePrescriptionUseCase = deletePrescriptionUseCase ?? throw new ArgumentNullException(nameof(deletePrescriptionUseCase));
        _prescriptions = new ObservableCollection<PrescriptionListItemDto>();

        CreateCommand = new RelayCommand(ExecuteCreate);
        ViewDetailsCommand = new RelayCommand<PrescriptionListItemDto>(ExecuteViewDetails);
        EditCommand = new RelayCommand<PrescriptionListItemDto>(ExecuteEdit);
        DeleteCommand = new RelayCommand<PrescriptionListItemDto>(async (p) => await ExecuteDeleteAsync(p));
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

            var prescriptions = await _listPrescriptionsUseCase.ExecuteAsync(
                new ListPrescriptionsByCustomerQuery { CustomerId = CustomerId });

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
        FormViewModel = new PrescriptionFormViewModel(_createPrescriptionUseCase, _updatePrescriptionUseCase)
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
    private void ExecuteViewDetails(PrescriptionListItemDto? prescription)
    {
        if (prescription == null) return;

        DetailViewModel = new PrescriptionDetailViewModel
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
    private void ExecuteEdit(PrescriptionListItemDto? prescription)
    {
        if (prescription == null) return;

        FormViewModel = new PrescriptionFormViewModel(_createPrescriptionUseCase, _updatePrescriptionUseCase);
        FormViewModel.LoadPrescription(prescription);
        FormViewModel.PrescriptionSaved += OnPrescriptionSaved;
        FormViewModel.Cancelled += OnFormCancelled;

        IsInEditMode = true;
    }

    /// <summary>
    /// Supprime une ordonnance.
    /// </summary>
    private async Task ExecuteDeleteAsync(PrescriptionListItemDto? prescription)
    {
        if (prescription == null) return;

        // TODO: Ajouter confirmation
        try
        {
            var result = await _deletePrescriptionUseCase.ExecuteAsync(
                new DeletePrescriptionCommand { PrescriptionId = prescription.PrescriptionId });

            if (!result.PrescriptionFound)
            {
                ErrorMessage = "L'ordonnance à supprimer est introuvable.";
                return;
            }

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

    private void OnEditRequested(object? sender, PrescriptionListItemDto prescription)
    {
        IsShowingDetail = false;
        ExecuteEdit(prescription);
    }

    private async void OnDeleteRequested(object? sender, PrescriptionListItemDto prescription)
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
