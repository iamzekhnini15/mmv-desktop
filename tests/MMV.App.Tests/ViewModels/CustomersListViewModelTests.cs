using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-3 — Comportement de présentation de <see cref="CustomersListViewModel"/> après extraction du flux de
/// <b>suppression de client</b> vers <see cref="IDeleteCustomerUseCase"/>.
///
/// La VM ne supprime plus directement via <c>ICustomerRepository.DeleteAsync</c> ni
/// <c>IUnitOfWork.SaveChangesAsync</c> (elle ne dépend plus de <c>IUnitOfWork</c> et n'expose plus
/// publiquement <c>Repository</c>/<c>UnitOfWork</c>) : elle construit une <see cref="DeleteCustomerCommand"/>
/// et <b>délègue</b>, tout en conservant la mise à jour d'écran (retrait de la liste, message d'erreur).
///
/// La couverture de persistance (vrai SQLite : suppression effective, introuvable) est portée par
/// <c>MMV.Application.Tests.DeleteCustomerUseCaseTests</c>.
///
/// <para>Note : le flux d'origine ne comporte aucun dialogue de confirmation dans la VM (le code-behind appelle
/// directement <c>DeleteCommand.Execute</c>) — il n'y a donc pas de chemin « annulation » à tester côté VM.</para>
/// </summary>
public class CustomersListViewModelTests
{
    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpyDeleteCustomerUseCase : IDeleteCustomerUseCase
    {
        private int _executeCount;
        private readonly Func<DeleteCustomerCommand, Task<DeleteCustomerResult>> _behavior;

        public SpyDeleteCustomerUseCase(Func<DeleteCustomerCommand, Task<DeleteCustomerResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(new DeleteCustomerResult { CustomerFound = true, CustomerId = cmd.CustomerId }));

        public int ExecuteCount => _executeCount;
        public DeleteCustomerCommand? LastCommand { get; private set; }

        public Task<DeleteCustomerResult> ExecuteAsync(DeleteCustomerCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }
    }

    private static Mock<ICustomerRepository> RepositoryReturning(params Customer[] customers)
    {
        var repo = new Mock<ICustomerRepository>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(customers);
        return repo;
    }

    /// <summary>
    /// Construit la VM, attend le chargement initial (LoadCustomersAsync appelé dans le constructeur) puis
    /// sélectionne le client fourni (la même instance que celle chargée dans la liste).
    /// </summary>
    private static async Task<CustomersListViewModel> BuildWithSelectedAsync(
        Customer customer, SpyDeleteCustomerUseCase spy)
    {
        var repo = RepositoryReturning(customer);
        var vm = new CustomersListViewModel(repo.Object, spy);
        await WaitUntilAsync(() => vm.Customers.Count == 1);
        vm.SelectedCustomer = vm.Customers[0];
        return vm;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "La condition attendue n'a pas été atteinte dans le délai imparti.");
    }

    private static Customer NewCustomer(long id = 7) => new()
    {
        CustomerId = id,
        FirstName = "Jean",
        LastName = "Dupont",
    };

    // ------------------------------------------------------------------
    // (1) Suppression : le use case est appelé et le client retiré de la liste
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_DelegatesToUseCase_AndRemovesFromList()
    {
        var spy = new SpyDeleteCustomerUseCase();
        var vm = await BuildWithSelectedAsync(NewCustomer(), spy);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => spy.ExecuteCount > 0);
        await WaitUntilAsync(() => vm.Customers.Count == 0);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Empty(vm.Customers);
        Assert.Null(vm.SelectedCustomer);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ------------------------------------------------------------------
    // (2) Mapping état VM → DeleteCustomerCommand (identifiant du client)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_BuildsCommandFromSelectedCustomer()
    {
        var spy = new SpyDeleteCustomerUseCase();
        var vm = await BuildWithSelectedAsync(NewCustomer(99), spy);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => spy.ExecuteCount > 0);

        Assert.NotNull(spy.LastCommand);
        Assert.Equal(99, spy.LastCommand!.CustomerId);
    }

    // ------------------------------------------------------------------
    // (3) Client introuvable → message d'erreur, client conservé, pas de crash
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WhenCustomerNotFound_ShowsError_AndKeepsCustomer()
    {
        var spy = new SpyDeleteCustomerUseCase(
            cmd => Task.FromResult(new DeleteCustomerResult { CustomerFound = false, CustomerId = cmd.CustomerId }));
        var vm = await BuildWithSelectedAsync(NewCustomer(), spy);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Single(vm.Customers);
        Assert.NotNull(vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // (4) Exception propagée par le use case → ErrorMessage (format existant)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_WhenUseCaseThrows_ShowsErrorMessage()
    {
        var spy = new SpyDeleteCustomerUseCase(
            _ => Task.FromException<DeleteCustomerResult>(
                new InvalidOperationException("Erreur de suppression inattendue.")));
        var vm = await BuildWithSelectedAsync(NewCustomer(), spy);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(1, spy.ExecuteCount);
        Assert.StartsWith("Erreur lors de la suppression du client : ", vm.ErrorMessage);
        Assert.Single(vm.Customers);
    }

    // ------------------------------------------------------------------
    // (5) DeleteCommand non exécutable sans client sélectionné
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteCommand_CannotExecute_WhenNoSelection()
    {
        var spy = new SpyDeleteCustomerUseCase();
        var repo = RepositoryReturning();
        var vm = new CustomersListViewModel(repo.Object, spy);
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.False(vm.DeleteCommand.CanExecute(null));
    }

    // ------------------------------------------------------------------
    // (6) Garde-fous constructeur
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CustomersListViewModel(customerRepository: null!, new SpyDeleteCustomerUseCase()));
    }

    [Fact]
    public void Constructor_WithoutDeleteUseCase_Throws()
    {
        var repo = RepositoryReturning();
        Assert.Throws<ArgumentNullException>(() =>
            new CustomersListViewModel(repo.Object, deleteCustomerUseCase: null!));
    }
}
