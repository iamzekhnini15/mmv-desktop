using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.Tests.TestDoubles;
using MMV.App.Time;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P4-5D — <see cref="PrescriptionFormViewModel"/> à la frontière temporelle
/// (ADR-PROD-DB-004 §5 décisions 5, 6 et 7 ; obligation T6).
///
/// <para>
/// Deux comportements y sont vérifiés, tous deux invérifiables avant ce lot :
/// </para>
/// <list type="number">
///   <item><description>la date d'émission par défaut vient de l'<b>horloge injectée</b>, et non de
///   <c>DateTimeOffset.Now</c> — donc un test peut la fixer ;</description></item>
///   <item><description>la date transmise au use case est la <b>date civile saisie</b>, et non sa
///   projection UTC — c'est le décalage d'un jour que la décision 7 supprime.</description></item>
/// </list>
/// </summary>
public class PrescriptionFormCivilDateTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    private static readonly DateTime Instant = new(2026, 9, 18, 23, 30, 0, DateTimeKind.Utc);

    private sealed class CapturingCreateUseCase : ICreatePrescriptionUseCase
    {
        public CreatePrescriptionCommand? LastCommand { get; private set; }

        public Task<CreatePrescriptionResult> ExecuteAsync(
            CreatePrescriptionCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(new CreatePrescriptionResult { PrescriptionId = 1 });
        }
    }

    private sealed class CapturingUpdateUseCase : IUpdatePrescriptionUseCase
    {
        public UpdatePrescriptionCommand? LastCommand { get; private set; }

        public Task<UpdatePrescriptionResult> ExecuteAsync(
            UpdatePrescriptionCommand command, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(new UpdatePrescriptionResult
            {
                PrescriptionFound = true,
                PrescriptionId = command.PrescriptionId
            });
        }
    }

    private sealed class NoopDialogService : IDialogService
    {
        public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(true);

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

        public Task ShowInformationAsync(string title, string message) => Task.CompletedTask;
    }

    /// <summary>
    /// <c>SaveCommand</c> est une <c>RelayCommand</c> synchrone qui lance une tâche : le dépôt attend donc
    /// la retombée plutôt que de l'attendre par <c>await</c>. Même mécanisme que la suite existante.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    private static PrescriptionFormViewModel Create(
        ICreatePrescriptionUseCase create,
        IUpdatePrescriptionUseCase update,
        DateOnly? localToday = null)
        => new(create, update, new NoopDialogService(), new FixedClock(Instant, localToday ?? Today));

    [Fact]
    public void LaDateParDefaut_VientDeLHorlogeInjectee()
    {
        var vm = Create(new CapturingCreateUseCase(), new CapturingUpdateUseCase());

        Assert.Equal(Today, DatePickerCivilDate.ToCivilDate(vm.IssueDate));
    }

    [Fact]
    public void LaDateParDefaut_EstLaDateCIVILEDuPoste_PasCelleDUtc()
    {
        // Le cas qui justifie IClock.LocalToday. À 23:30 UTC le 18, un poste en UTC+2 est déjà le 19 : une
        // ordonnance qu'il saisit porte la date du 19, pas du 18. Avec un simple DateOnly dérivé d'UtcNow,
        // le formulaire aurait proposé la veille.
        var vm = Create(new CapturingCreateUseCase(), new CapturingUpdateUseCase(),
            localToday: new DateOnly(2026, 9, 19));

        Assert.Equal(new DateOnly(2026, 9, 19), DatePickerCivilDate.ToCivilDate(vm.IssueDate));
        Assert.NotEqual(DateOnly.FromDateTime(Instant), DatePickerCivilDate.ToCivilDate(vm.IssueDate));
    }

    [Fact]
    public async Task LaCreation_TransmetLaDateCivileSaisie_SansDecalage()
    {
        // Régression directe de l'ancien « IssueDate = IssueDate.UtcDateTime » : la saisie du 15 mars sous
        // un fuseau en avance sur UTC arrivait au use case datée du 14.
        var create = new CapturingCreateUseCase();
        var vm = Create(create, new CapturingUpdateUseCase());
        vm.CustomerId = 7;
        vm.IssueDate = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.FromHours(2));

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => create.LastCommand is not null);

        Assert.NotNull(create.LastCommand);
        Assert.Equal(new DateOnly(2026, 3, 15), create.LastCommand!.IssueDate);
    }

    [Fact]
    public async Task LaModification_TransmetAussiLaDateCivileSaisie()
    {
        var update = new CapturingUpdateUseCase();
        var vm = Create(new CapturingCreateUseCase(), update);
        vm.LoadPrescription(new PrescriptionListItemDto
        {
            PrescriptionId = 55,
            CustomerId = 7,
            IssueDate = new DateOnly(2025, 1, 1),
            DoctorName = "Dr House"
        });
        vm.IssueDate = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.FromHours(2));

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => update.LastCommand is not null);

        Assert.NotNull(update.LastCommand);
        Assert.Equal(new DateOnly(2026, 3, 15), update.LastCommand!.IssueDate);
    }

    [Fact]
    public void LeChargementDUneOrdonnance_AfficheSaDateExacte()
    {
        // Sens lecture : ouvrir une fiche ne doit pas décaler ce qu'elle affiche — c'est par ce chemin que
        // l'ancienne conversion corrompait les données, un jour par ouverture-enregistrement.
        var vm = Create(new CapturingCreateUseCase(), new CapturingUpdateUseCase());

        vm.LoadPrescription(new PrescriptionListItemDto
        {
            PrescriptionId = 55,
            CustomerId = 7,
            IssueDate = new DateOnly(2025, 1, 1)
        });

        Assert.Equal(new DateOnly(2025, 1, 1), DatePickerCivilDate.ToCivilDate(vm.IssueDate));
    }

    [Fact]
    public async Task UnAllerRetourSansModification_NeChangePasLaDate()
    {
        // Le scénario réel de corruption : charger, enregistrer, sans toucher au champ de date.
        var update = new CapturingUpdateUseCase();
        var vm = Create(new CapturingCreateUseCase(), update);
        vm.LoadPrescription(new PrescriptionListItemDto
        {
            PrescriptionId = 55,
            CustomerId = 7,
            IssueDate = new DateOnly(2025, 1, 1)
        });

        vm.SaveCommand.Execute(null);
        await WaitUntilAsync(() => update.LastCommand is not null);

        Assert.Equal(new DateOnly(2025, 1, 1), update.LastCommand!.IssueDate);
    }

    [Fact]
    public void LaViewModel_AccepteUneHorlogeAbsente_EtRetombeSurLHorlogeSysteme()
    {
        // Le paramètre est optionnel : les sites de construction existants (navigation, code-behind) ne
        // passent pas d'horloge. Ce test fige ce contrat — s'il devenait obligatoire, il faudrait le
        // décider, pas le découvrir à la compilation d'un appelant.
        var vm = new PrescriptionFormViewModel(
            new CapturingCreateUseCase(), new CapturingUpdateUseCase(), new NoopDialogService());

        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), DatePickerCivilDate.ToCivilDate(vm.IssueDate));
    }
}
