using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Customers.DeleteCustomer;
using MMV.Application.UseCases.Customers.ListCustomers;
using MMV.Application.UseCases.Customers.SetCustomerArchived;
using MMV.Domain.Exceptions;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2C-3 — Comportement de présentation de <see cref="CustomersListViewModel"/> après extraction du flux de
/// <b>suppression de client</b> vers <see cref="IDeleteCustomerUseCase"/>.
///
/// P3-2C — Finalisation UI du domaine Clients : <b>confirmation explicite</b> avant suppression physique,
/// <b>refus métier</b> (client porteur d'historique) affiché proprement et proposant l'archivage,
/// <b>archivage / réactivation</b> via <see cref="ISetCustomerArchivedUseCase"/>, filtre <b>actifs / archivés</b>
/// via <see cref="ListCustomersQuery.IncludeArchived"/>, et <b>rechargement depuis la source</b> après toute
/// mutation (la collection locale n'est jamais la vérité — un autre poste a pu modifier le client).
///
/// La couverture de persistance (vrai SQLite : suppression effective, refus par clé étrangère, idempotence de
/// l'archivage) est portée par <c>MMV.Application.Tests</c>.
/// </summary>
public class CustomersListViewModelTests
{
    // ==================================================================
    // Doublures
    // ==================================================================

    /// <summary>Espion de lecture : compte les appels et mémorise les queries reçues (donc IncludeArchived).</summary>
    private sealed class SpyListCustomersUseCase : IListCustomersUseCase
    {
        private readonly Func<int, IReadOnlyList<CustomerListItemDto>> _resultsByCall;
        private int _executeCount;

        public SpyListCustomersUseCase(params CustomerListItemDto[] customers)
            => _resultsByCall = _ => customers;

        /// <param name="resultsByCall">Résultat renvoyé en fonction du numéro d'appel (1 = chargement initial).</param>
        public SpyListCustomersUseCase(Func<int, IReadOnlyList<CustomerListItemDto>> resultsByCall)
            => _resultsByCall = resultsByCall;

        public int ExecuteCount => Volatile.Read(ref _executeCount);
        public List<ListCustomersQuery> Queries { get; } = new();
        public ListCustomersQuery? LastQuery
        {
            get { lock (Queries) { return Queries.Count == 0 ? null : Queries[^1]; } }
        }

        public Task<IReadOnlyList<CustomerListItemDto>> ExecuteAsync(
            ListCustomersQuery query, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _executeCount);
            lock (Queries) { Queries.Add(query); }
            return Task.FromResult(_resultsByCall(call));
        }
    }

    /// <summary>Espion de use case : compte les appels, mémorise la dernière commande, joue un comportement.</summary>
    private sealed class SpyDeleteCustomerUseCase : IDeleteCustomerUseCase
    {
        private int _executeCount;
        private readonly Func<DeleteCustomerCommand, Task<DeleteCustomerResult>> _behavior;

        public SpyDeleteCustomerUseCase(Func<DeleteCustomerCommand, Task<DeleteCustomerResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(new DeleteCustomerResult { CustomerFound = true, CustomerId = cmd.CustomerId }));

        public int ExecuteCount => Volatile.Read(ref _executeCount);
        public DeleteCustomerCommand? LastCommand { get; private set; }

        public Task<DeleteCustomerResult> ExecuteAsync(DeleteCustomerCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }
    }

    /// <summary>Espion d'archivage : mémorise l'état absolu demandé (IsArchived), jamais un basculement.</summary>
    private sealed class SpySetCustomerArchivedUseCase : ISetCustomerArchivedUseCase
    {
        private int _executeCount;
        private readonly Func<SetCustomerArchivedCommand, Task<SetCustomerArchivedResult>> _behavior;

        public SpySetCustomerArchivedUseCase(Func<SetCustomerArchivedCommand, Task<SetCustomerArchivedResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(new SetCustomerArchivedResult
            {
                CustomerFound = true,
                CustomerId = cmd.CustomerId,
                IsArchived = cmd.IsArchived,
            }));

        public int ExecuteCount => Volatile.Read(ref _executeCount);
        public SetCustomerArchivedCommand? LastCommand { get; private set; }

        public Task<SetCustomerArchivedResult> ExecuteAsync(SetCustomerArchivedCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            LastCommand = command;
            return _behavior(command);
        }
    }

    /// <summary>Dialogue simulé : réponse de confirmation programmable, appels enregistrés.</summary>
    private sealed class StubDialogService : IDialogService
    {
        private readonly bool _confirmationResult;

        public StubDialogService(bool confirmationResult = true) => _confirmationResult = confirmationResult;

        public int ConfirmationCount { get; private set; }
        public string? LastConfirmationMessage { get; private set; }
        public int ErrorCount { get; private set; }
        public string? LastErrorMessage { get; private set; }

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            ConfirmationCount++;
            LastConfirmationMessage = message;
            return Task.FromResult(_confirmationResult);
        }

        public Task ShowErrorAsync(string title, string message)
        {
            ErrorCount++;
            LastErrorMessage = message;
            return Task.CompletedTask;
        }

        public Task ShowInformationAsync(string title, string message) => Task.CompletedTask;
    }

    // ==================================================================
    // Fabriques
    // ==================================================================

    private static CustomerListItemDto NewCustomer(long id = 7, bool isArchived = false) => new()
    {
        CustomerId = id,
        FirstName = "Jean",
        LastName = "Dupont",
        IsArchived = isArchived,
    };

    private static CustomersListViewModel Build(
        SpyListCustomersUseCase list,
        SpyDeleteCustomerUseCase? delete = null,
        SpySetCustomerArchivedUseCase? archive = null,
        StubDialogService? dialog = null)
        => new(list, delete ?? new SpyDeleteCustomerUseCase(), archive ?? new SpySetCustomerArchivedUseCase(),
            dialog ?? new StubDialogService());

    /// <summary>
    /// Construit la VM, attend le chargement initial (LoadCustomersAsync appelé dans le constructeur) puis
    /// sélectionne le premier client chargé (la même instance que celle de la liste).
    /// </summary>
    private static async Task<CustomersListViewModel> BuildWithSelectedAsync(
        SpyListCustomersUseCase list,
        SpyDeleteCustomerUseCase? delete = null,
        SpySetCustomerArchivedUseCase? archive = null,
        StubDialogService? dialog = null)
    {
        var vm = Build(list, delete, archive, dialog);
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

    // ==================================================================
    // (A) Suppression — confirmation explicite
    // ==================================================================

    [Fact]
    public async Task Delete_WhenConfirmed_DelegatesToUseCase()
    {
        var list = new SpyListCustomersUseCase(NewCustomer(99));
        var spy = new SpyDeleteCustomerUseCase();
        var dialog = new StubDialogService(confirmationResult: true);
        var vm = await BuildWithSelectedAsync(list, spy, dialog: dialog);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => spy.ExecuteCount > 0);

        Assert.Equal(1, dialog.ConfirmationCount);
        Assert.Equal(CustomersListViewModel.DeleteConfirmationMessage, dialog.LastConfirmationMessage);
        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(99, spy.LastCommand!.CustomerId);
    }

    [Fact]
    public async Task Delete_WhenConfirmationRefused_DoesNotCallUseCase_AndChangesNothing()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());
        var spy = new SpyDeleteCustomerUseCase();
        var dialog = new StubDialogService(confirmationResult: false);
        var vm = await BuildWithSelectedAsync(list, spy, dialog: dialog);

        var readsBefore = list.ExecuteCount;
        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => dialog.ConfirmationCount > 0);
        await Task.Delay(50); // laisse au flux le temps d'agir s'il le faisait à tort

        Assert.Equal(0, spy.ExecuteCount);          // aucun appel au use case
        Assert.Single(vm.Customers);                // aucun changement local
        Assert.Equal(readsBefore, list.ExecuteCount); // aucun rechargement
        Assert.NotNull(vm.SelectedCustomer);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ==================================================================
    // (B) Suppression — rechargement depuis la source (multi-poste)
    // ==================================================================

    [Fact]
    public async Task Delete_WhenSucceeds_ReloadsFromUseCase()
    {
        // 1er appel (chargement initial) : le client est là ; après suppression, la source ne le renvoie plus.
        var list = new SpyListCustomersUseCase(call =>
            call == 1 ? new[] { NewCustomer() } : Array.Empty<CustomerListItemDto>());
        var spy = new SpyDeleteCustomerUseCase();
        var vm = await BuildWithSelectedAsync(list, spy);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => list.ExecuteCount >= 2);
        await WaitUntilAsync(() => vm.Customers.Count == 0);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(2, list.ExecuteCount);   // rechargement, pas un simple Remove local
        Assert.Empty(vm.Customers);
        Assert.Null(vm.SelectedCustomer);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Delete_WhenCustomerNotFound_ShowsClearMessage_AndReloads()
    {
        // Supprimé depuis un autre poste : la source ne le renvoie plus au rechargement.
        var list = new SpyListCustomersUseCase(call =>
            call == 1 ? new[] { NewCustomer() } : Array.Empty<CustomerListItemDto>());
        var spy = new SpyDeleteCustomerUseCase(
            cmd => Task.FromResult(new DeleteCustomerResult { CustomerFound = false, CustomerId = cmd.CustomerId }));
        var vm = await BuildWithSelectedAsync(list, spy);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(1, spy.ExecuteCount);
        Assert.Equal(CustomersListViewModel.CustomerNotFoundMessage, vm.ErrorMessage);
        Assert.Equal(2, list.ExecuteCount);   // rechargement malgré l'échec
        Assert.Empty(vm.Customers);
    }

    // ==================================================================
    // (C) Suppression — refus métier (client porteur d'historique)
    // ==================================================================

    [Fact]
    public async Task Delete_WhenCustomerHasHistory_ShowsBusinessMessage_KeepsCustomer_AndReloads()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());   // le client reste présent en base
        var spy = new SpyDeleteCustomerUseCase(
            _ => Task.FromException<DeleteCustomerResult>(
                new BusinessRuleException(DeleteCustomerUseCase.CustomerHasHistoryMessage)));
        var dialog = new StubDialogService(confirmationResult: true);
        var vm = await BuildWithSelectedAsync(list, spy, dialog: dialog);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        // Message métier exact, tiré de la constante publique du use case (pas d'une chaîne dupliquée).
        Assert.Equal(DeleteCustomerUseCase.CustomerHasHistoryMessage, vm.ErrorMessage);

        // Le message propose l'archivage et ne fuit aucun détail technique.
        Assert.Contains("Archivez-le", vm.ErrorMessage!);
        Assert.DoesNotContain("Exception", vm.ErrorMessage!);
        Assert.DoesNotContain("Erreur lors de la suppression", vm.ErrorMessage!);

        // Le client n'est PAS retiré de la collection locale, et la liste est rechargée depuis la source.
        Assert.Single(vm.Customers);
        Assert.Equal(2, list.ExecuteCount);
        Assert.Equal(1, dialog.ErrorCount);
        Assert.Equal(DeleteCustomerUseCase.CustomerHasHistoryMessage, dialog.LastErrorMessage);
    }

    [Fact]
    public async Task Delete_WhenUnexpectedError_ShowsGenericMessage()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());
        var spy = new SpyDeleteCustomerUseCase(
            _ => Task.FromException<DeleteCustomerResult>(
                new InvalidOperationException("Erreur de suppression inattendue.")));
        var dialog = new StubDialogService(confirmationResult: true);
        var vm = await BuildWithSelectedAsync(list, spy, dialog: dialog);

        vm.DeleteCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        // Traitement générique, distinct du refus métier.
        Assert.StartsWith("Erreur lors de la suppression du client : ", vm.ErrorMessage);
        Assert.Single(vm.Customers);
        Assert.Equal(0, dialog.ErrorCount);
    }

    // ==================================================================
    // (D) Archivage
    // ==================================================================

    [Fact]
    public async Task Archive_SendsAbsoluteState_IsArchivedTrue()
    {
        var list = new SpyListCustomersUseCase(NewCustomer(42, isArchived: false));
        var archive = new SpySetCustomerArchivedUseCase();
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        vm.ArchiveCommand.Execute(null);
        await WaitUntilAsync(() => archive.ExecuteCount > 0);

        Assert.Equal(1, archive.ExecuteCount);
        Assert.Equal(42, archive.LastCommand!.CustomerId);
        Assert.True(archive.LastCommand.IsArchived);
    }

    [Fact]
    public async Task Archive_WhenSucceeds_ReloadsFromUseCase()
    {
        // Vue « actifs seulement » : une fois archivé, le client disparaît de la lecture.
        var list = new SpyListCustomersUseCase(call =>
            call == 1 ? new[] { NewCustomer() } : Array.Empty<CustomerListItemDto>());
        var archive = new SpySetCustomerArchivedUseCase();
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        vm.ArchiveCommand.Execute(null);
        await WaitUntilAsync(() => list.ExecuteCount >= 2);

        Assert.Equal(2, list.ExecuteCount);
        Assert.Empty(vm.Customers);
        Assert.Null(vm.SelectedCustomer);     // plus visible : sélection non restaurée
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task Archive_WhenCustomerNotFound_ShowsClearMessage()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());
        var archive = new SpySetCustomerArchivedUseCase(
            cmd => Task.FromResult(new SetCustomerArchivedResult { CustomerFound = false, CustomerId = cmd.CustomerId }));
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        vm.ArchiveCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(CustomersListViewModel.ArchivedCustomerNotFoundMessage, vm.ErrorMessage);
        Assert.Equal(1, archive.ExecuteCount);
    }

    [Fact]
    public async Task Archive_Twice_StillSendsAbsoluteTrue_NeverToggles()
    {
        // Idempotence applicative conservée : la VM redemande l'état ABSOLU voulu, elle ne bascule jamais.
        // Le client reste archivé côté source (IncludeArchived = true), donc réarchiver redemande bien « true ».
        var list = new SpyListCustomersUseCase(call =>
            call == 1 ? new[] { NewCustomer(5, isArchived: false) } : new[] { NewCustomer(5, isArchived: true) });
        var archive = new SpySetCustomerArchivedUseCase();
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        // 1er archivage sur le client actif affiché.
        vm.ArchiveCommand.Execute(null);
        await WaitUntilAsync(() => archive.ExecuteCount == 1);
        Assert.True(archive.LastCommand!.IsArchived);

        // Le client rechargé est archivé : c'est Réactiver qui s'applique, et il demande « false ».
        await WaitUntilAsync(() => vm.Customers.Count == 1 && vm.Customers[0].IsArchived);
        vm.ReactivateCommand.Execute(vm.Customers[0]);
        await WaitUntilAsync(() => archive.ExecuteCount == 2);

        Assert.False(archive.LastCommand!.IsArchived);   // état absolu, jamais un toggle aveugle
    }

    // ==================================================================
    // (E) Réactivation
    // ==================================================================

    [Fact]
    public async Task Reactivate_SendsAbsoluteState_IsArchivedFalse()
    {
        var list = new SpyListCustomersUseCase(NewCustomer(42, isArchived: true));
        var archive = new SpySetCustomerArchivedUseCase();
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        vm.ReactivateCommand.Execute(null);
        await WaitUntilAsync(() => archive.ExecuteCount > 0);

        Assert.Equal(1, archive.ExecuteCount);
        Assert.Equal(42, archive.LastCommand!.CustomerId);
        Assert.False(archive.LastCommand.IsArchived);
    }

    [Fact]
    public async Task Reactivate_WhenSucceeds_ReloadsAndKeepsSelectionVisible()
    {
        var list = new SpyListCustomersUseCase(call =>
            call == 1 ? new[] { NewCustomer(5, isArchived: true) } : new[] { NewCustomer(5, isArchived: false) });
        var archive = new SpySetCustomerArchivedUseCase();
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        vm.ReactivateCommand.Execute(null);
        await WaitUntilAsync(() => list.ExecuteCount >= 2);
        await WaitUntilAsync(() => vm.SelectedCustomer is { IsArchived: false });

        Assert.Equal(2, list.ExecuteCount);
        Assert.Single(vm.Customers);
        Assert.False(vm.SelectedCustomer!.IsArchived);   // sélection restaurée sur l'instance rechargée
        Assert.Equal(5, vm.SelectedCustomer.CustomerId);
    }

    [Fact]
    public async Task Reactivate_WhenCustomerNotFound_ShowsClearMessage()
    {
        var list = new SpyListCustomersUseCase(NewCustomer(isArchived: true));
        var archive = new SpySetCustomerArchivedUseCase(
            cmd => Task.FromResult(new SetCustomerArchivedResult { CustomerFound = false, CustomerId = cmd.CustomerId }));
        var vm = await BuildWithSelectedAsync(list, archive: archive);

        vm.ReactivateCommand.Execute(null);
        await WaitUntilAsync(() => !string.IsNullOrEmpty(vm.ErrorMessage));

        Assert.Equal(CustomersListViewModel.ArchivedCustomerNotFoundMessage, vm.ErrorMessage);
        Assert.Equal(1, archive.ExecuteCount);
    }

    // ==================================================================
    // (F) Filtre actifs / archivés — appliqué EN BASE, pas sur la collection locale
    // ==================================================================

    [Fact]
    public async Task Load_ByDefault_QueriesWithoutArchived()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());
        var vm = Build(list);
        await WaitUntilAsync(() => list.ExecuteCount >= 1);

        Assert.False(vm.IncludeArchived);
        Assert.False(list.LastQuery!.IncludeArchived);
    }

    [Fact]
    public async Task IncludeArchived_WhenEnabled_RequeriesWithIncludeArchivedTrue()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());
        var vm = Build(list);
        await WaitUntilAsync(() => list.ExecuteCount >= 1);

        vm.IncludeArchived = true;
        await WaitUntilAsync(() => list.ExecuteCount >= 2);

        Assert.True(list.LastQuery!.IncludeArchived);
    }

    [Fact]
    public async Task IncludeArchived_WhenDisabledAgain_RequeriesWithIncludeArchivedFalse()
    {
        var list = new SpyListCustomersUseCase(NewCustomer());
        var vm = Build(list);
        await WaitUntilAsync(() => list.ExecuteCount >= 1);

        vm.IncludeArchived = true;
        await WaitUntilAsync(() => list.ExecuteCount >= 2);

        vm.IncludeArchived = false;
        await WaitUntilAsync(() => list.ExecuteCount >= 3);

        Assert.False(list.LastQuery!.IncludeArchived);
    }

    [Fact]
    public async Task IncludeArchived_WhenEnabled_ArchivedCustomerIsIdentifiableInList()
    {
        var list = new SpyListCustomersUseCase(call => call == 1
            ? new[] { NewCustomer(1) }
            : new[] { NewCustomer(1), NewCustomer(2, isArchived: true) });
        var vm = Build(list);
        await WaitUntilAsync(() => vm.Customers.Count == 1);

        vm.IncludeArchived = true;
        await WaitUntilAsync(() => vm.Customers.Count == 2);

        Assert.Equal(2, vm.FilteredCustomers.Count);
        Assert.Single(vm.FilteredCustomers, c => c.IsArchived);   // l'archivé est distinguable à l'écran
        Assert.Equal(2, vm.FilteredCustomers.Single(c => c.IsArchived).CustomerId);
    }

    // ==================================================================
    // (G) Disponibilité des commandes (confort d'écran — l'autorité reste le use case)
    // ==================================================================

    [Fact]
    public async Task Commands_CannotExecute_WhenNoSelection()
    {
        var list = new SpyListCustomersUseCase();
        var vm = Build(list);
        await WaitUntilAsync(() => !vm.IsLoading);

        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.ArchiveCommand.CanExecute(null));
        Assert.False(vm.ReactivateCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public async Task Commands_ForActiveCustomer_AllowDeleteAndArchive_ButNotReactivate()
    {
        var list = new SpyListCustomersUseCase(NewCustomer(isArchived: false));
        var vm = await BuildWithSelectedAsync(list);

        Assert.True(vm.DeleteCommand.CanExecute(null));
        Assert.True(vm.ArchiveCommand.CanExecute(null));
        Assert.False(vm.ReactivateCommand.CanExecute(null));
        Assert.True(vm.EditCommand.CanExecute(null));
    }

    [Fact]
    public async Task Commands_ForArchivedCustomer_AllowReactivate_ButNotDeleteArchiveOrEdit()
    {
        var list = new SpyListCustomersUseCase(NewCustomer(isArchived: true));
        var vm = await BuildWithSelectedAsync(list);

        Assert.True(vm.ReactivateCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.False(vm.ArchiveCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(null));   // éditer exige de réactiver d'abord
    }

    // ==================================================================
    // (H) Architecture — aucune dépendance de persistance dans le ViewModel
    // ==================================================================

    [Fact]
    public void Constructor_TakesNoPersistenceDependency()
    {
        var parameterTypes = typeof(CustomersListViewModel)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.FullName ?? p.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain(parameterTypes, t =>
            t.Contains("Repository", StringComparison.Ordinal) ||
            t.Contains("UnitOfWork", StringComparison.Ordinal) ||
            t.Contains("DbContext", StringComparison.Ordinal) ||
            t.Contains("MMV.Infrastructure", StringComparison.Ordinal));
    }

    [Fact]
    public void ViewModel_ExposesNoPersistenceSurface()
    {
        // Aucune propriété publique n'expose un repository / UnitOfWork / DbContext (régression P2C-3 à préserver) :
        // la VM ne parle qu'aux use cases de la couche Application et au service de dialogue de la couche UI.
        var propertyTypes = typeof(CustomersListViewModel)
            .GetProperties()
            .Select(p => p.PropertyType.FullName ?? p.PropertyType.Name)
            .ToList();

        Assert.DoesNotContain(propertyTypes, t =>
            t.Contains("Repository", StringComparison.Ordinal) ||
            t.Contains("UnitOfWork", StringComparison.Ordinal) ||
            t.Contains("DbContext", StringComparison.Ordinal) ||
            t.Contains("MMV.Infrastructure", StringComparison.Ordinal));
    }

    // ==================================================================
    // (I) Garde-fous constructeur
    // ==================================================================

    [Fact]
    public void Constructor_WithoutListUseCase_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CustomersListViewModel(
            listCustomersUseCase: null!, new SpyDeleteCustomerUseCase(),
            new SpySetCustomerArchivedUseCase(), new StubDialogService()));

    [Fact]
    public void Constructor_WithoutDeleteUseCase_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CustomersListViewModel(
            new SpyListCustomersUseCase(), deleteCustomerUseCase: null!,
            new SpySetCustomerArchivedUseCase(), new StubDialogService()));

    [Fact]
    public void Constructor_WithoutSetArchivedUseCase_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CustomersListViewModel(
            new SpyListCustomersUseCase(), new SpyDeleteCustomerUseCase(),
            setCustomerArchivedUseCase: null!, new StubDialogService()));

    [Fact]
    public void Constructor_WithoutDialogService_Throws()
        => Assert.Throws<ArgumentNullException>(() => new CustomersListViewModel(
            new SpyListCustomersUseCase(), new SpyDeleteCustomerUseCase(),
            new SpySetCustomerArchivedUseCase(), dialogService: null!));
}
