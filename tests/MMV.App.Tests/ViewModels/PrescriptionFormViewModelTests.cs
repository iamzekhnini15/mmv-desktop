using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.Common;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-4 / P3-3C — Comportement de présentation de <see cref="PrescriptionFormViewModel"/>.
///
/// P2C-4 : la VM ne persiste plus directement — elle construit une commande et <b>délègue</b> (création si le
/// formulaire est vierge, mise à jour si une ordonnance existante a été chargée).
///
/// P3-3C : la VM n'est plus <b>propriétaire d'aucune règle métier</b> (le Domain l'est depuis P3-3B). Elle envoie la
/// commande telle que saisie, puis <b>affiche</b> le verdict du use case : <c>ValidationErrors</c>, client introuvable
/// (<c>CustomerFound = false</c>), ordonnance introuvable (<c>PrescriptionFound = false</c>) et refus métier
/// « client archivé » (<see cref="BusinessRuleException"/>). Sur tout refus, le formulaire reste ouvert, les valeurs
/// saisies sont conservées, et aucun succès n'est signalé.
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

    private sealed class StubDialogService : IDialogService
    {
        public string? LastErrorTitle { get; private set; }
        public string? LastErrorMessage { get; private set; }
        public int ErrorCount { get; private set; }

        public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(true);

        public Task ShowErrorAsync(string title, string message)
        {
            ErrorCount++;
            LastErrorTitle = title;
            LastErrorMessage = message;
            return Task.CompletedTask;
        }

        public Task ShowInformationAsync(string title, string message) => Task.CompletedTask;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    private static PrescriptionFormViewModel BuildCreateForm(
        SpyCreatePrescriptionUseCase create, SpyUpdatePrescriptionUseCase update, IDialogService? dialog = null)
        => new(create, update, dialog ?? new StubDialogService())
        {
            CustomerId = 7,
            DoctorName = "Dr House"
        };

    private static CreatePrescriptionResult Invalid(params string[] messages) => new()
    {
        ValidationErrors = messages.Select(m => new ValidationError("OdAxis", m)).ToList()
    };

    // ------------------------------------------------------------------
    // (1) Création : délégation au CreatePrescriptionUseCase + événement
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenNew_DelegatesToCreateUseCase_AndRaisesSaved()
    {
        var create = new SpyCreatePrescriptionUseCase();
        var update = new SpyUpdatePrescriptionUseCase();
        var vm = BuildCreateForm(create, update);

        Prescription? saved = null;
        vm.PrescriptionSaved += (_, p) => saved = p;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        Assert.Equal(1, create.ExecuteCount);
        Assert.Equal(0, update.ExecuteCount);
        Assert.Equal(7, create.LastCommand!.CustomerId);
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
        var vm = new PrescriptionFormViewModel(create, update, new StubDialogService());

        vm.LoadPrescription(new PrescriptionListItemDto
        {
            PrescriptionId = 55,
            CustomerId = 7,
            IssueDate = new DateOnly(2025, 1, 1),
            DoctorName = "Dr House"
        });

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => update.ExecuteCount > 0);

        Assert.Equal(1, update.ExecuteCount);
        Assert.Equal(0, create.ExecuteCount);
        Assert.Equal(55, update.LastCommand!.PrescriptionId);
    }

    // ==================================================================
    // P3-3C — PrismBase : saisie par sélection, sur l'enum Domain réel
    // ==================================================================

    [Fact]
    public void PrismBaseOptions_ExposeTheFourRealEnumValues_AndAnEmptyOption()
    {
        var values = PrescriptionFormViewModel.PrismBaseOptions.Select(o => o.Value).ToList();

        // Les quatre valeurs réelles de l'enum Domain, plus l'absence de base (null).
        Assert.Equal(
            new PrismBase?[] { null, PrismBase.In, PrismBase.Out, PrismBase.Up, PrismBase.Down },
            values);

        // La liste couvre exactement l'enum : aucune valeur métier oubliée, aucune valeur inventée.
        Assert.Equal(
            Enum.GetValues<PrismBase>().Cast<PrismBase?>().OrderBy(v => v).ToList(),
            values.Where(v => v.HasValue).OrderBy(v => v).ToList());
    }

    [Fact]
    public void PrismBaseOptions_ContainNoHorizontalOrVerticalValue()
    {
        // Le watermark historique « H/V/In/Out » proposait des valeurs qui n'existent pas dans l'enum.
        var labels = PrescriptionFormViewModel.PrismBaseOptions.Select(o => o.Label).ToList();

        Assert.DoesNotContain("H", labels);
        Assert.DoesNotContain("V", labels);
        Assert.Equal(5, PrescriptionFormViewModel.PrismBaseOptions.Count);
    }

    [Fact]
    public void SelectingEmptyOption_ProducesNullPrismBase()
    {
        var vm = BuildCreateForm(new SpyCreatePrescriptionUseCase(), new SpyUpdatePrescriptionUseCase());
        var emptyOption = PrescriptionFormViewModel.PrismBaseOptions.Single(o => o.Value is null);

        vm.OdPrismBase = PrismBase.In;
        vm.SelectedOdPrismBaseOption = emptyOption;

        Assert.Null(vm.OdPrismBase);
        Assert.Null(vm.SelectedOdPrismBaseOption!.Value);
        Assert.Equal("Aucune", vm.SelectedOdPrismBaseOption.Label);
    }

    [Theory]
    [InlineData(PrismBase.In)]
    [InlineData(PrismBase.Out)]
    [InlineData(PrismBase.Up)]
    [InlineData(PrismBase.Down)]
    public async Task Save_SendsSelectedPrismBase_AsDomainEnum_ForBothEyes(PrismBase selected)
    {
        var create = new SpyCreatePrescriptionUseCase();
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());
        var option = PrescriptionFormViewModel.PrismBaseOptions.Single(o => o.Value == selected);

        vm.SelectedOdPrismBaseOption = option;
        vm.SelectedOgPrismBaseOption = option;
        vm.OdPrismValue = 2;
        vm.OgPrismValue = 2;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        // La valeur persistée est l'enum Domain, jamais le libellé français affiché.
        Assert.Equal(selected, create.LastCommand!.OdPrismBase);
        Assert.Equal(selected, create.LastCommand!.OgPrismBase);
    }

    [Fact]
    public async Task PrismBase_OdAndOg_AreIndependent()
    {
        var create = new SpyCreatePrescriptionUseCase();
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());

        vm.SelectedOdPrismBaseOption = PrescriptionFormViewModel.PrismBaseOptions.Single(o => o.Value == PrismBase.Up);
        vm.OdPrismValue = 1;
        // OG reste sans base.

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        Assert.Equal(PrismBase.Up, create.LastCommand!.OdPrismBase);
        Assert.Null(create.LastCommand!.OgPrismBase);
    }

    // ==================================================================
    // P3-3C — DoctorName facultatif (aligné sur le Domain, P3-3B §10)
    // ==================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Save_WithoutDoctorName_StillDelegates(string? doctorName)
    {
        var create = new SpyCreatePrescriptionUseCase();
        var vm = new PrescriptionFormViewModel(create, new SpyUpdatePrescriptionUseCase(), new StubDialogService())
        {
            CustomerId = 7,
            DoctorName = doctorName!
        };

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        // Le médecin est facultatif : aucune obligation UI ne bloque plus l'enregistrement.
        Assert.Equal(1, create.ExecuteCount);
        Assert.Empty(vm.ValidationMessages);
        Assert.DoesNotContain("médecin", vm.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ViewModel_NoLongerExposesADoctorNameError()
    {
        // L'ancien message « Le nom du médecin est requis » vivait dans une propriété DoctorNameError, qui n'a plus
        // de raison d'être : la règle n'existe ni dans le Domain, ni dans l'Application.
        Assert.Null(typeof(PrescriptionFormViewModel).GetProperty("DoctorNameError"));
    }

    // ==================================================================
    // P3-3C — Affichage des ValidationErrors du résultat
    // ==================================================================

    [Fact]
    public async Task Save_WhenResultInvalid_ShowsErrors_KeepsForm_AndDoesNotRaiseSaved()
    {
        var create = new SpyCreatePrescriptionUseCase(_ => Task.FromResult(Invalid("L'axe est requis lorsqu'un cylindre est saisi.")));
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());
        vm.OdCylinder = -1.25;
        vm.Notes = "Ne doit pas disparaître";

        var raised = false;
        vm.PrescriptionSaved += (_, _) => raised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => vm.HasValidationErrors);

        Assert.Contains("L'axe est requis lorsqu'un cylindre est saisi.", vm.ValidationMessages);
        Assert.False(raised);
        // Le formulaire n'est ni fermé ni vidé : toutes les valeurs saisies sont conservées.
        Assert.Equal(-1.25, vm.OdCylinder);
        Assert.Equal("Ne doit pas disparaître", vm.Notes);
        Assert.Equal(7, vm.CustomerId);
    }

    [Fact]
    public async Task Save_WhenSeveralErrors_ShowsThemAll_WithoutDuplicates_AndInOrder()
    {
        var create = new SpyCreatePrescriptionUseCase(
            _ => Task.FromResult(Invalid("Erreur A", "Erreur B", "Erreur A")));
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => vm.HasValidationErrors);

        Assert.Equal(new[] { "Erreur A", "Erreur B" }, vm.ValidationMessages);
    }

    [Fact]
    public async Task Save_DoesNotApplyAnyLocalBusinessRule_ThatTheUseCaseWouldAccept()
    {
        // Les plages (sphère, cylindre, addition, axe) appartiennent au Domain depuis P3-3B. La VM ne doit plus
        // refuser localement une commande que le use case accepte : elle délègue, toujours.
        var create = new SpyCreatePrescriptionUseCase();
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());

        vm.OdSphere = -99;     // hors plage Domain : c'est au use case de trancher, pas à l'écran
        vm.OdCylinder = 42;
        vm.OdAddition = 12;
        vm.OdAxis = 999;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        Assert.Equal(1, create.ExecuteCount);
        Assert.Equal(-99, create.LastCommand!.OdSphere);
        Assert.Equal(999, create.LastCommand!.OdAxis);
    }

    // ==================================================================
    // P3-3C — Client introuvable (CustomerFound = false)
    // ==================================================================

    [Fact]
    public async Task Save_WhenCustomerNotFound_ShowsClearMessage_KeepsForm_AndDoesNotRaiseSaved()
    {
        var create = new SpyCreatePrescriptionUseCase(
            _ => Task.FromResult(new CreatePrescriptionResult { CustomerFound = false }));
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());
        vm.Notes = "Conservé";

        var raised = false;
        vm.PrescriptionSaved += (_, _) => raised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(PrescriptionFormViewModel.CustomerNotFoundMessage, vm.ErrorMessage);
        Assert.False(raised);
        Assert.Equal("Conservé", vm.Notes);
        // Aucun détail technique : ni DbUpdateException, ni nom d'exception.
        Assert.DoesNotContain("Exception", vm.ErrorMessage!);
    }

    // ==================================================================
    // P3-3C — Client archivé (BusinessRuleException)
    // ==================================================================

    [Fact]
    public async Task Save_WhenCustomerArchived_ShowsExactBusinessMessage_KeepsForm_AndDoesNotRaiseSaved()
    {
        var create = new SpyCreatePrescriptionUseCase(
            _ => Task.FromException<CreatePrescriptionResult>(
                new BusinessRuleException(CreatePrescriptionUseCase.CustomerArchivedMessage)));
        var dialog = new StubDialogService();
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase(), dialog);
        vm.OdSphere = -1.5;

        var raised = false;
        vm.PrescriptionSaved += (_, _) => raised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        // Message métier exact, tiré de la constante publique du use case (pas d'une chaîne dupliquée).
        Assert.Equal(CreatePrescriptionUseCase.CustomerArchivedMessage, vm.ErrorMessage);
        Assert.Contains("Réactivez-le", vm.ErrorMessage!);
        Assert.DoesNotContain("BusinessRuleException", vm.ErrorMessage!);
        Assert.DoesNotContain("Erreur lors de la sauvegarde", vm.ErrorMessage!);

        // Le refus est rendu incontournable par le mécanisme de dialogue existant.
        await WaitUntilAsync(() => dialog.ErrorCount > 0);
        Assert.Equal(CreatePrescriptionUseCase.CustomerArchivedMessage, dialog.LastErrorMessage);

        Assert.False(raised);
        Assert.Equal(-1.5, vm.OdSphere);
    }

    [Fact]
    public async Task Save_WhenUnexpectedError_ShowsGenericMessage_DistinctFromBusinessRefusal()
    {
        var create = new SpyCreatePrescriptionUseCase(
            _ => Task.FromException<CreatePrescriptionResult>(new InvalidOperationException("boom")));
        var dialog = new StubDialogService();
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase(), dialog);

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.StartsWith("Erreur lors de la sauvegarde : ", vm.ErrorMessage);
        // Une erreur technique n'est pas un refus métier : elle ne passe pas par le dialogue de refus.
        Assert.Equal(0, dialog.ErrorCount);
    }

    // ==================================================================
    // P3-3C — Mise à jour
    // ==================================================================

    [Fact]
    public async Task Save_WhenUpdateNotFound_ShowsClearMessage_AndDoesNotRaiseSaved()
    {
        var update = new SpyUpdatePrescriptionUseCase(
            cmd => Task.FromResult(new UpdatePrescriptionResult { PrescriptionFound = false, PrescriptionId = cmd.PrescriptionId }));
        var vm = new PrescriptionFormViewModel(new SpyCreatePrescriptionUseCase(), update, new StubDialogService());
        vm.LoadPrescription(new PrescriptionListItemDto { PrescriptionId = 55, CustomerId = 7, IssueDate = new DateOnly(2025, 1, 1), DoctorName = "Dr House" });

        var raised = false;
        vm.PrescriptionSaved += (_, _) => raised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.False(raised);
        Assert.Equal(PrescriptionFormViewModel.PrescriptionNotFoundMessage, vm.ErrorMessage);
    }

    [Fact]
    public async Task Save_WhenUpdateInvalid_ShowsErrors_KeepsForm_AndDoesNotRaiseSaved()
    {
        var update = new SpyUpdatePrescriptionUseCase(
            cmd => Task.FromResult(new UpdatePrescriptionResult
            {
                PrescriptionFound = true,
                PrescriptionId = cmd.PrescriptionId,
                ValidationErrors = new List<ValidationError> { new("OgAxis", "L'axe est incohérent.") }
            }));
        var vm = new PrescriptionFormViewModel(new SpyCreatePrescriptionUseCase(), update, new StubDialogService());
        vm.LoadPrescription(new PrescriptionListItemDto { PrescriptionId = 55, CustomerId = 7, IssueDate = new DateOnly(2025, 1, 1), DoctorName = "Dr House" });
        vm.Notes = "Conservé";

        var raised = false;
        vm.PrescriptionSaved += (_, _) => raised = true;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => vm.HasValidationErrors);

        Assert.Contains("L'axe est incohérent.", vm.ValidationMessages);
        Assert.False(raised);
        Assert.Equal("Conservé", vm.Notes);
    }

    [Fact]
    public async Task Save_WhenCustomerArchived_UpdateIsStillAllowed()
    {
        // P3-3B §15 : la correction d'une ordonnance existante reste autorisée pour un client archivé. L'UI n'ajoute
        // aucun blocage local fondé sur IsArchived.
        var update = new SpyUpdatePrescriptionUseCase();
        var vm = new PrescriptionFormViewModel(new SpyCreatePrescriptionUseCase(), update, new StubDialogService());
        vm.LoadPrescription(new PrescriptionListItemDto { PrescriptionId = 55, CustomerId = 7, IssueDate = new DateOnly(2025, 1, 1) });

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => update.ExecuteCount > 0);

        Assert.Equal(1, update.ExecuteCount);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ==================================================================
    // P3-3C — Axe 0 : aucune normalisation locale
    // ==================================================================

    [Fact]
    public async Task Save_SendsAxisZero_WithoutLocalNormalization()
    {
        // La valeur canonique (0 → 180) est produite par le use case (OpticalAxisNormalizer, P3-3B). La VM n'anticipe
        // rien : elle envoie 0 tel quel, et la vérité revient par le rechargement du parent depuis la source.
        var create = new SpyCreatePrescriptionUseCase();
        var vm = BuildCreateForm(create, new SpyUpdatePrescriptionUseCase());
        vm.OdCylinder = -1.0;
        vm.OdAxis = 0;

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.ExecuteCount > 0);

        // 0 part tel quel : la VM ne le corrige pas en 180 avant l'appel.
        Assert.Equal(0, create.LastCommand!.OdAxis);
        Assert.NotEqual(180, create.LastCommand!.OdAxis);
    }

    // ------------------------------------------------------------------
    // Garde-fous constructeur
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutCreateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PrescriptionFormViewModel(createPrescriptionUseCase: null!, new SpyUpdatePrescriptionUseCase(), new StubDialogService()));
    }

    [Fact]
    public void Constructor_WithoutUpdateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PrescriptionFormViewModel(new SpyCreatePrescriptionUseCase(), updatePrescriptionUseCase: null!, new StubDialogService()));
    }

    [Fact]
    public void Constructor_WithoutDialogService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PrescriptionFormViewModel(new SpyCreatePrescriptionUseCase(), new SpyUpdatePrescriptionUseCase(), dialogService: null!));
    }
}
