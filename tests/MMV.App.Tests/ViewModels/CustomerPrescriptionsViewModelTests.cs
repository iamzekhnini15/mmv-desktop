using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Application.UseCases.Prescriptions.DeletePrescription;
using MMV.Application.UseCases.Prescriptions.UpdatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-4 — Comportement de présentation de <see cref="CustomerPrescriptionsViewModel"/> après extraction de la
/// <b>suppression d'ordonnance</b> vers <see cref="IDeletePrescriptionUseCase"/>.
///
/// La VM ne supprime plus directement (plus de <c>IPrescriptionRepository.DeleteAsync</c> ni
/// <c>IUnitOfWork.CommitAsync</c>, et ne dépend plus de <c>IUnitOfWork</c>) : elle construit une
/// <see cref="DeletePrescriptionCommand"/> et <b>délègue</b>. Elle conserve <c>IPrescriptionRepository</c>
/// uniquement pour les lectures d'affichage (chargement de la liste).
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

    private static Mock<IPrescriptionRepository> RepositoryReturning(params Prescription[] prescriptions)
    {
        var repo = new Mock<IPrescriptionRepository>();
        repo.Setup(r => r.GetByCustomerIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescriptions);
        return repo;
    }

    private static CustomerPrescriptionsViewModel Build(
        Mock<IPrescriptionRepository> repo, IDeletePrescriptionUseCase delete)
        => new(repo.Object, Mock.Of<ICreatePrescriptionUseCase>(), Mock.Of<IUpdatePrescriptionUseCase>(), delete);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(10);
        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    private static Prescription NewPrescription(long id = 42) =>
        new() { PrescriptionId = id, CustomerId = 7, DoctorName = "Dr House" };

    // ------------------------------------------------------------------
    // (1) Suppression : délégation au use case + mapping de l'identifiant
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_DelegatesToUseCase_WithSelectedPrescriptionId()
    {
        var prescription = NewPrescription(99);
        var repo = RepositoryReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase();
        var vm = Build(repo, spy);
        await vm.InitializeAsync(7);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => spy.ExecuteCount > 0);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(99, spy.LastCommand!.PrescriptionId);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ------------------------------------------------------------------
    // (2) Ordonnance introuvable → message d'erreur, pas de crash
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WhenNotFound_ShowsError()
    {
        var prescription = NewPrescription();
        var repo = RepositoryReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase(
            cmd => Task.FromResult(new DeletePrescriptionResult { PrescriptionFound = false, PrescriptionId = cmd.PrescriptionId }));
        var vm = Build(repo, spy);
        await vm.InitializeAsync(7);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal("L'ordonnance à supprimer est introuvable.", vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (3) Exception du use case → ErrorMessage (format existant)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WhenUseCaseThrows_ShowsErrorMessage()
    {
        var prescription = NewPrescription();
        var repo = RepositoryReturning(prescription);
        var spy = new SpyDeletePrescriptionUseCase(
            _ => Task.FromException<DeletePrescriptionResult>(new InvalidOperationException("boom")));
        var vm = Build(repo, spy);
        await vm.InitializeAsync(7);

        vm.DeleteCommand.Execute(prescription);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.StartsWith("Erreur lors de la suppression : ", vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Garde-fous constructeur
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            prescriptionRepository: null!, Mock.Of<ICreatePrescriptionUseCase>(),
            Mock.Of<IUpdatePrescriptionUseCase>(), new SpyDeletePrescriptionUseCase()));
    }

    [Fact]
    public void Constructor_WithoutCreateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            RepositoryReturning().Object, createPrescriptionUseCase: null!,
            Mock.Of<IUpdatePrescriptionUseCase>(), new SpyDeletePrescriptionUseCase()));
    }

    [Fact]
    public void Constructor_WithoutUpdateUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            RepositoryReturning().Object, Mock.Of<ICreatePrescriptionUseCase>(),
            updatePrescriptionUseCase: null!, new SpyDeletePrescriptionUseCase()));
    }

    [Fact]
    public void Constructor_WithoutDeleteUseCase_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CustomerPrescriptionsViewModel(
            RepositoryReturning().Object, Mock.Of<ICreatePrescriptionUseCase>(),
            Mock.Of<IUpdatePrescriptionUseCase>(), deletePrescriptionUseCase: null!));
    }
}
