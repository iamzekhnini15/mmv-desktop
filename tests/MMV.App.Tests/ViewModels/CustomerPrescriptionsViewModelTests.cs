using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-4 / P2D-5 / P3-3C — Comportement de présentation de <see cref="CustomerPrescriptionsViewModel"/>.
///
/// P2C-4 / P2D-5 : la VM ne supprime plus directement et ne lit plus via un repository — elle délègue aux use cases
/// Application, qui renvoient des DTO plats.
///
/// P3-3C : la suppression <b>physique</b> d'une ordonnance exige désormais une <b>confirmation explicite</b>
/// (<see cref="IDialogService"/>, patron P3-2C), et toute mutation — comme tout constat « introuvable » — est suivie
/// d'un <b>rechargement depuis la source</b> (multi-poste, ADR-PROD-DB-001) : la collection locale n'est jamais la
/// vérité.
/// </summary>
public class CustomerPrescriptionsViewModelTests
{
    private sealed class SpyDeletePrescriptionUseCase : IDeletePrescriptionUseCase
    {
        private readonly Func<DeletePrescriptionCommand, Task<DeletePrescriptionResult>> _behavior;
        public SpyDeletePrescriptionUseCase(Func<DeletePrescriptionCommand, Task<DeletePrescriptionResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(new DeletePrescriptionResult { PrescriptionFound = true, PrescriptionId = cmd.PrescriptionId }));

        public int ExecuteCount { get; private set; }
        public DeletePrescriptionCommand? LastCommand { get; private set; }

        public Task<DeletePrescriptionResult> ExecuteAsync(DeletePrescriptionCommand command, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            LastCommand = command;
            return _behavior(command);
        }
    }

    private sealed class StubDialogService : IDialogService
    {
        private readonly bool _confirms;
        public StubDialogService(bool confirms = true) => _confirms = confirms;

        public int ConfirmationCount { get; private set; }
        public string? LastConfirmationTitle { get; private set; }
        public string? LastConfirmationMessage { get; private set; }

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            ConfirmationCount++;
            LastConfirmationTitle = title;
            LastConfirmationMessage = message;
            return Task.FromResult(_confirms);
        }

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowInformationAsync(string title, string message) => Task.CompletedTask;
    }

    private static Mock<IListPrescriptionsByCustomerUseCase> ListUseCaseReturning(params PrescriptionListItemDto[] prescriptions)
    {
        var useCase = new Mock<IListPrescriptionsByCustomerUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<ListPrescriptionsByCustomerQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescriptions);
        return useCase;
    }

    /// <summary>Use case de création qui réussit (les refus sont couverts par PrescriptionFormViewModelTests).</summary>
    private static ICreatePrescriptionUseCase SucceedingCreate()
    {
        var useCase = new Mock<ICreatePrescriptionUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<CreatePrescriptionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePrescriptionResult { PrescriptionId = 123 });
        return useCase.Object;
    }

    /// <summary>Use case de mise à jour qui réussit.</summary>
    private static IUpdatePrescriptionUseCase SucceedingUpdate()
    {
        var useCase = new Mock<IUpdatePrescriptionUseCase>();
        useCase.Setup(u => u.ExecuteAsync(It.IsAny<UpdatePrescriptionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdatePrescriptionCommand cmd, CancellationToken _) =>
                new UpdatePrescriptionResult { PrescriptionFound = true, PrescriptionId = cmd.PrescriptionId });
        return useCase.Object;
    }

    private static CustomerPrescriptionsViewModel Build(
        Mock<IListPrescriptionsByCustomerUseCase> list,
        IDeletePrescriptionUseCase delete,
        IDialogService? dialog = null)
        => new(list.Object, SucceedingCreate(), SucceedingUpdate(),
            delete, dialog ?? new StubDialogService());

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    /// <summary>Nombre d'appels au use case de lecture (preuve du rechargement depuis la source).</summary>
    private static int LoadCount(Mock<IListPrescriptionsByCustomerUseCase> list) =>
        list.Invocations.Count(i => i.Method.Name == nameof(IListPrescriptionsByCustomerUseCase.ExecuteAsync));

    private static PrescriptionListItemDto NewPrescription(long id = 42) =>
        new() { PrescriptionId = id, CustomerId = 7, IssueDate = new DateOnly(2026, 1, 15), DoctorName = "Dr House" };

    // ==================================================================
    // P3-3C — Confirmation avant suppression physique
    // ==================================================================

    [Fact]
    public async Task Delete_WhenConfirmed_DelegatesToUseCase_WithTargetedPrescriptionId()
    {
        var prescription = NewPrescription(99);
        var list = ListUseCaseReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase();
        var dialog = new StubDialogService(confirms: true);
        var vm = Build(list, spy, dialog);
        await vm.InitializeAsync(7);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => spy.ExecuteCount > 0);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(99, spy.LastCommand!.PrescriptionId);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Delete_ShowsExpectedConfirmationTitleAndMessage()
    {
        var prescription = NewPrescription();
        var dialog = new StubDialogService(confirms: true);
        var vm = Build(ListUseCaseReturning(prescription), new SpyDeletePrescriptionUseCase(), dialog);
        await vm.InitializeAsync(7);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => dialog.ConfirmationCount > 0);

        Assert.Equal(CustomerPrescriptionsViewModel.DeleteConfirmationTitle, dialog.LastConfirmationTitle);
        Assert.Equal(CustomerPrescriptionsViewModel.DeleteConfirmationMessage, dialog.LastConfirmationMessage);
        // Le caractère irréversible de la suppression physique est explicite.
        Assert.Contains("irréversible", dialog.LastConfirmationMessage!);
    }

    [Fact]
    public async Task Delete_WhenCancelled_CallsNothing_AndChangesNothing()
    {
        var prescription = NewPrescription(99);
        var list = ListUseCaseReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase();
        var dialog = new StubDialogService(confirms: false);
        var vm = Build(list, spy, dialog);
        await vm.InitializeAsync(7);

        var loadsAfterInit = LoadCount(list);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => dialog.ConfirmationCount > 0);

        // Annulation : aucun appel au use case, aucun rechargement, aucune mutation locale, aucun message de réussite.
        Assert.Equal(0, spy.ExecuteCount);
        Assert.Equal(loadsAfterInit, LoadCount(list));
        Assert.Single(vm.Prescriptions);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ==================================================================
    // P3-3C — Rechargement depuis la source (multi-poste)
    // ==================================================================

    [Fact]
    public async Task Delete_WhenSucceeded_ReloadsFromSource()
    {
        var prescription = NewPrescription(99);
        var list = ListUseCaseReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase();
        var vm = Build(list, spy);
        await vm.InitializeAsync(7);

        var loadsAfterInit = LoadCount(list);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => LoadCount(list) > loadsAfterInit);

        // L'état affiché est reconstruit depuis la source partagée, jamais par un Remove local.
        Assert.Equal(loadsAfterInit + 1, LoadCount(list));
    }

    [Fact]
    public async Task Delete_WhenNotFound_ShowsClearMessage_AndReloadsFromSource()
    {
        var prescription = NewPrescription();
        var list = ListUseCaseReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase(
            cmd => Task.FromResult(new DeletePrescriptionResult { PrescriptionFound = false, PrescriptionId = cmd.PrescriptionId }));
        var vm = Build(list, spy);
        await vm.InitializeAsync(7);

        var loadsAfterInit = LoadCount(list);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(CustomerPrescriptionsViewModel.PrescriptionNotFoundMessage, vm.ErrorMessage);
        // Supprimée depuis un autre poste : on repart de l'état partagé.
        Assert.Equal(loadsAfterInit + 1, LoadCount(list));
    }

    [Fact]
    public async Task Delete_WhenUseCaseThrows_ShowsGenericMessage()
    {
        var prescription = NewPrescription();
        var spy = new SpyDeletePrescriptionUseCase(
            _ => Task.FromException<DeletePrescriptionResult>(new InvalidOperationException("boom")));
        var vm = Build(ListUseCaseReturning(prescription), spy);
        await vm.InitializeAsync(7);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.StartsWith("Erreur lors de la suppression : ", vm.ErrorMessage);
    }

    [Fact]
    public async Task Delete_NeverRemovesFromLocalCollectionAsSourceOfTruth()
    {
        // La source renvoie toujours l'ordonnance (ex. : un autre poste l'a recréée / la suppression n'a pas pris) :
        // la liste affichée doit refléter la source, pas l'intention locale.
        var prescription = NewPrescription(99);
        var list = ListUseCaseReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase();
        var vm = Build(list, spy);
        await vm.InitializeAsync(7);

        var loadsAfterInit = LoadCount(list);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => LoadCount(list) > loadsAfterInit);

        Assert.Single(vm.Prescriptions);
        Assert.Equal(99, vm.Prescriptions[0].PrescriptionId);
    }

    [Fact]
    public async Task AfterCreation_ReloadsFromSource()
    {
        var list = ListUseCaseReturning(NewPrescription());
        var vm = Build(list, new SpyDeletePrescriptionUseCase());
        await vm.InitializeAsync(7);

        var loadsAfterInit = LoadCount(list);

        // Le formulaire est ouvert par la VM parente, qui s'abonne à PrescriptionSaved.
        vm.CreateCommand.Execute(null);
        Assert.NotNull(vm.FormViewModel);

        vm.FormViewModel!.SaveCommand.Execute(null);

        await WaitUntilAsync(() => LoadCount(list) > loadsAfterInit);

        Assert.False(vm.IsInEditMode);
        Assert.Null(vm.FormViewModel);
    }

    [Fact]
    public async Task AfterUpdate_ReloadsFromSource()
    {
        var prescription = NewPrescription(55);
        var list = ListUseCaseReturning(prescription);
        var vm = Build(list, new SpyDeletePrescriptionUseCase());
        await vm.InitializeAsync(7);

        var loadsAfterInit = LoadCount(list);

        vm.EditCommand.Execute(prescription);
        Assert.NotNull(vm.FormViewModel);

        vm.FormViewModel!.SaveCommand.Execute(null);
        await WaitUntilAsync(() => LoadCount(list) > loadsAfterInit);

        Assert.Equal(loadsAfterInit + 1, LoadCount(list));
        Assert.False(vm.IsInEditMode);
    }

    // ==================================================================
    // Hygiène de dépendances (garde-fou explicite, cf. AppUiPersistenceGuardrailTests)
    // ==================================================================

    [Fact]
    public void ViewModels_TakeNoPersistenceDependency()
    {
        var forbidden = new[] { "Repository", "IUnitOfWork", "DbContext" };

        foreach (var type in new[] { typeof(CustomerPrescriptionsViewModel), typeof(PrescriptionFormViewModel) })
        {
            var parameterTypes = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType.Name);
            var propertyTypes = type.GetProperties().Select(p => p.PropertyType.Name);

            foreach (var name in parameterTypes.Concat(propertyTypes))
            {
                Assert.DoesNotContain(forbidden, f => name.Contains(f, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    // ------------------------------------------------------------------
    // Garde-fous constructeur
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutListUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            listPrescriptionsUseCase: null!, Mock.Of<ICreatePrescriptionUseCase>(),
            Mock.Of<IUpdatePrescriptionUseCase>(), new SpyDeletePrescriptionUseCase(), new StubDialogService()));
    }

    [Fact]
    public void Constructor_WithoutCreateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            ListUseCaseReturning().Object, createPrescriptionUseCase: null!,
            Mock.Of<IUpdatePrescriptionUseCase>(), new SpyDeletePrescriptionUseCase(), new StubDialogService()));
    }

    [Fact]
    public void Constructor_WithoutUpdateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            ListUseCaseReturning().Object, Mock.Of<ICreatePrescriptionUseCase>(),
            updatePrescriptionUseCase: null!, new SpyDeletePrescriptionUseCase(), new StubDialogService()));
    }

    [Fact]
    public void Constructor_WithoutDeleteUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            ListUseCaseReturning().Object, Mock.Of<ICreatePrescriptionUseCase>(),
            Mock.Of<IUpdatePrescriptionUseCase>(), deletePrescriptionUseCase: null!, new StubDialogService()));
    }

    [Fact]
    public void Constructor_WithoutDialogService_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            ListUseCaseReturning().Object, Mock.Of<ICreatePrescriptionUseCase>(),
            Mock.Of<IUpdatePrescriptionUseCase>(), new SpyDeletePrescriptionUseCase(), dialogService: null!));
    }
}
