using MMV.App.ViewModels;
using MMV.Domain.Entities;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-4 — <see cref="PrescriptionDetailViewModel"/> est purement présentationnel : il ne déclenche que des
/// événements (édition / suppression / retour) et ne réalise aucune persistance. Sa dépendance
/// <c>IPrescriptionRepository</c> (jamais utilisée) a été retirée en P2C-4 ; il se construit désormais sans
/// dépendance de persistance.
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
        var prescription = new Prescription { PrescriptionId = 42 };
        var vm = new PrescriptionDetailViewModel { CurrentPrescription = prescription };

        Prescription? requested = null;
        vm.DeleteRequested += (_, p) => requested = p;

        vm.DeleteCommand.Execute(null);

        Assert.Same(prescription, requested);
    }

    [Fact]
    public void EditCommand_RaisesEditRequested_WithCurrentPrescription()
    {
        var prescription = new Prescription { PrescriptionId = 42 };
        var vm = new PrescriptionDetailViewModel { CurrentPrescription = prescription };

        Prescription? requested = null;
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
