using System;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour afficher les détails d'une ordonnance.
/// </summary>
public class PrescriptionDetailViewModel : BaseViewModel
{
    private PrescriptionListItemDto? _currentPrescription;

    /// <summary>
    /// Ordonnance actuelle.
    /// </summary>
    // P2D-5 : la fiche détaillée consomme désormais un DTO applicatif plat (PrescriptionListItemDto), plus l'entité EF
    // Prescription. Les liaisons compilées de PrescriptionDetailView (CurrentPrescription.Od*/Og*/Notes…) restent
    // valides : les noms et types de propriétés du DTO sont identiques à ceux de l'entité.
    public PrescriptionListItemDto? CurrentPrescription
    {
        get => _currentPrescription;
        set => SetProperty(ref _currentPrescription, value);
    }

    /// <summary>
    /// Commande pour revenir à la liste.
    /// </summary>
    public ICommand BackCommand { get; }

    /// <summary>
    /// Commande pour modifier l'ordonnance.
    /// </summary>
    public ICommand EditCommand { get; }

    /// <summary>
    /// Commande pour supprimer l'ordonnance.
    /// </summary>
    public ICommand DeleteCommand { get; }

    /// <summary>
    /// Événement déclenché pour demander l'édition.
    /// </summary>
    public event EventHandler<PrescriptionListItemDto>? EditRequested;

    /// <summary>
    /// Événement déclenché pour demander la suppression.
    /// </summary>
    public event EventHandler<PrescriptionListItemDto>? DeleteRequested;

    /// <summary>
    /// Événement déclenché pour revenir en arrière.
    /// </summary>
    public event EventHandler? BackRequested;

    public PrescriptionDetailViewModel()
    {
        BackCommand = new RelayCommand(ExecuteBack);
        EditCommand = new RelayCommand(ExecuteEdit);
        DeleteCommand = new RelayCommand(ExecuteDelete);

        Title = "Détail Ordonnance";
    }

    private void ExecuteBack()
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteEdit()
    {
        if (CurrentPrescription != null)
        {
            EditRequested?.Invoke(this, CurrentPrescription);
        }
    }

    private void ExecuteDelete()
    {
        if (CurrentPrescription != null)
        {
            DeleteRequested?.Invoke(this, CurrentPrescription);
        }
    }
}
