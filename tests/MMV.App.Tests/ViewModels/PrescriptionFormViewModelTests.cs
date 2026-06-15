using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-4 — Comportement de présentation de <see cref="PrescriptionFormViewModel"/> après extraction des écritures
/// d'ordonnance vers <see cref="ICreatePrescriptionUseCase"/> / <see cref="IUpdatePrescriptionUseCase"/>.
///
/// La VM ne persiste plus directement (plus de <c>IPrescriptionRepository.CreateAsync</c> ni
/// <c>IUnitOfWork.CommitAsync</c>) : elle construit une commande et <b>délègue</b> — création si le formulaire est
/// vierge, mise à jour si une ordonnance existante a été chargée (<c>PrescriptionId &gt; 0</c>).
///
/// La couverture de persistance (vrai SQLite) est portée par <c>MMV.Application.Tests</c>.
/// </summary>
public class PrescriptionFormViewModelTests
{
    private sealed class SpyCreatePrescriptionUseCase : ICreatePrescriptionUseCase
    {
        private readonly Func<CreatePrescriptionCommand, Task<CreatePrescriptionResult>> _behavior;
        public SpyCreatePrescriptionUseCase(Func<CreatePrescriptionCommand, Task<CreatePrescriptionResult>>? behavior = null)
            => _behavior = behavior ?? (_ => Task.FromResult(new CreatePrescriptionResult { PrescriptionId = 123 }));

        public int ExecuteCount { get; private set; }
        public CreatePrescriptionCommand? LastCommand { get; private set; }

        public Task<CreatePrescriptionResult> ExecuteAsync(CreatePrescriptionCommand command, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            LastCommand = command;
            return _behavior(command);
        }
    }

    private sealed class SpyUpdatePrescriptionUseCase : IUpdatePrescriptionUseCase
    {
        private readonly Func<UpdatePrescriptionCommand, Task<UpdatePrescriptionResult>> _behavior;
        public SpyUpdatePrescriptionUseCase(Func<UpdatePrescriptionCommand, Task<UpdatePrescriptionResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(new UpdatePrescriptionResult { PrescriptionFound = true, PrescriptionId = cmd.PrescriptionId }));

        public int ExecuteCount { get; private set; }
        public UpdatePrescriptionCommand? LastCommand { get; private set; }

        public Task<UpdatePrescriptionResult> ExecuteAsync(UpdatePrescriptionCommand command, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            LastCommand = command;
            return _behavior(command);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    private static PrescriptionFormViewModel BuildValidCreateForm(
        SpyCreatePrescriptionUseCase create, SpyUpdatePrescriptionUseCase update)
        => new(create, update)
        {
            CustomerId = 7,
            DoctorName = "Dr House" // requis par la validation du formulaire
        };

    // ------------------------------------------------------------------
    // (1) Création : délégation au CreatePrescriptionUseCase + événement
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenNew_DelegatesToCreateUseCase_AndRaisesSaved()
    {
        var create = new SpyCreatePrescriptionUseCase();
        var update = new SpyUpdatePrescriptionUseCase();
        var vm = BuildValidCreateForm(create, update);

        Prescription? saved = null;
        vm.PrescriptionSaved += (_, p) => saved = p;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        Assert.Equal(1, create.ExecuteCount);
        Assert.Equal(0, update.ExecuteCount);
        Assert.Equal(7, create.LastCommand!.CustomerId);
        Assert.Equal("Dr House", create.LastCommand!.DoctorName);
        await WaitUntilAsync(() => saved != null);
        Assert.Equal(123, saved!.PrescriptionId);
    }

    // ------------------------------------------------------------------
    // (2) Édition : délégation au UpdatePrescriptionUseCase
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenEditingExisting_DelegatesToUpdateUseCase()
    {
        var create = new SpyCreatePrescriptionUseCase();
        var update = new SpyUpdatePrescriptionUseCase();
        var vm = new PrescriptionFormViewModel(create, update);

        vm.LoadPrescription(new Prescription
        {
            PrescriptionId = 55,
            CustomerId = 7,
            IssueDate = new DateTime(2025, 1, 1),
            DoctorName = "Dr House"
        });

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => update.ExecuteCount > 0);

        Assert.Equal(1, update.ExecuteCount);
        Assert.Equal(0, create.ExecuteCount);
        Assert.Equal(55, update.LastCommand!.PrescriptionId);
    }

    // ------------------------------------------------------------------
    // (3) Mise à jour introuvable → message d'erreur, pas d'événement
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenUpdateNotFound_ShowsError_AndDoesNotRaiseSaved()
    {
        var create = new SpyCreatePrescriptionUseCase();
        var update = new SpyUpdatePrescriptionUseCase(
            cmd => Task.FromResult(new UpdatePrescriptionResult { PrescriptionFound = false, PrescriptionId = cmd.PrescriptionId }));
        var vm = new PrescriptionFormViewModel(create, update);
        vm.LoadPrescription(new Prescription { PrescriptionId = 55, CustomerId = 7, IssueDate = new DateTime(2025, 1, 1), DoctorName = "Dr House" });

        var raised = false;
        vm.PrescriptionSaved += (_, _) => raised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.False(raised);
        Assert.Equal("L'ordonnance à modifier est introuvable.", vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Exception du use case → ErrorMessage (format existant)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenUseCaseThrows_ShowsErrorMessage()
    {
        var create = new SpyCreatePrescriptionUseCase(
            _ => Task.FromException<CreatePrescriptionResult>(new InvalidOperationException("boom")));
        var update = new SpyUpdatePrescriptionUseCase();
        var vm = BuildValidCreateForm(create, update);

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.StartsWith("Erreur lors de la sauvegarde : ", vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (5) Formulaire invalide → aucune délégation
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenFormInvalid_DoesNotDelegate()
    {
        var create = new SpyCreatePrescriptionUseCase();
        var update = new SpyUpdatePrescriptionUseCase();
        // DoctorName vide → validation échoue (ValidateDoctorName).
        var vm = new PrescriptionFormViewModel(create, update) { CustomerId = 7 };

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(0, create.ExecuteCount);
        Assert.Equal(0, update.ExecuteCount);
    }

    // ------------------------------------------------------------------
    // (6) Garde-fous constructeur
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutCreateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PrescriptionFormViewModel(createPrescriptionUseCase: null!, new SpyUpdatePrescriptionUseCase()));
    }

    [Fact]
    public void Constructor_WithoutUpdateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PrescriptionFormViewModel(new SpyCreatePrescriptionUseCase(), updatePrescriptionUseCase: null!));
    }
}
