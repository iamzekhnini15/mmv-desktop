using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-4 / P2D-5 — <see cref="PrescriptionDetailViewModel"/> est purement présentationnel : il ne déclenche que des
/// événements (édition / suppression / retour) et ne réalise aucune persistance. Sa dépendance
/// <c>IPrescriptionRepository</c> (jamais utilisée) a été retirée en P2C-4 ; en P2D-5 il consomme un DTO applicatif
/// plat (<see cref="PrescriptionListItemDto"/>) au lieu de l'entité EF.
/// </summary>
public class PrescriptionDetailViewModelTests
{
    [Fact]
    public void Constructor_HasNoPersistenceDependency()
    {
        var vm = new PrescriptionDetailViewModel();
        Assert.Equal("Détail Ordonnance", vm.Title);
    }

    [Fact]
    public void DeleteCommand_RaisesDeleteRequested_WithCurrentPrescription()
    {
        var prescription = new PrescriptionListItemDto { PrescriptionId = 42 };
        var vm = new PrescriptionDetailViewModel { CurrentPrescription = prescription };

        PrescriptionListItemDto? requested = null;
        vm.DeleteRequested += (_, p) => requested = p;

        vm.DeleteCommand.Execute(null);

        Assert.Same(prescription, requested);
    }

    [Fact]
    public void EditCommand_RaisesEditRequested_WithCurrentPrescription()
    {
        var prescription = new PrescriptionListItemDto { PrescriptionId = 42 };
        var vm = new PrescriptionDetailViewModel { CurrentPrescription = prescription };

        PrescriptionListItemDto? requested = null;
        vm.EditRequested += (_, p) => requested = p;

        vm.EditCommand.Execute(null);

        Assert.Same(prescription, requested);
    }

    [Fact]
    public void DeleteCommand_WhenNoCurrentPrescription_DoesNotRaise()
    {
        var vm = new PrescriptionDetailViewModel();

        var raised = false;
        vm.DeleteRequested += (_, _) => raised = true;

        vm.DeleteCommand.Execute(null);

        Assert.False(raised);
    }
}
