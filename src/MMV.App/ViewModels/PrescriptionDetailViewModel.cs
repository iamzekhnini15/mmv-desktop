using System;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour afficher les détails d'une ordonnance.
/// </summary>
public class PrescriptionDetailViewModel : BaseViewModel
{
    private Prescription? _currentPrescription;

    /// <summary>
    /// Ordonnance actuelle.
    /// </summary>
    public Prescription? CurrentPrescription
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
    public event EventHandler<Prescription>? EditRequested;

    /// <summary>
    /// Événement déclenché pour demander la suppression.
    /// </summary>
    public event EventHandler<Prescription>? DeleteRequested;

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
