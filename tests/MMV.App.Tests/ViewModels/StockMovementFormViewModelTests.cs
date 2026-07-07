using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Application.UseCases.Products.ListProductsForPicker;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using Moq;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2B-2F — Comportement de présentation de <see cref="StockMovementFormViewModel"/> après extraction du flux de
/// création de <b>mouvement manuel de stock</b> vers <see cref="ICreateStockMovementUseCase"/>.
///
/// La VM ne récupère plus le produit, n'ouvre plus de transaction, ne décrémente / n'incrémente plus le stock et
/// ne crée plus le <c>StockMovement</c> directement : pour chaque ligne valide du formulaire multi-produits, elle
/// construit une <see cref="CreateStockMovementCommand"/> et <b>délègue</b>. Ces tests vérifient : (1) la
/// délégation effective + l'événement <c>MovementSaved</c> + le mapping ligne → commande ; (2) le libellé de
/// motif par défaut quand aucune note n'est saisie ; (3) le refus métier (stock insuffisant) propagé en message
/// utilisateur contrôlé sans <c>MovementSaved</c> ; (4) un produit introuvable compté en erreur ; (5) la boucle
/// multi-lignes ; (6) le rejet d'un use case absent ; (7) la garde <c>IsSaving</c>.
///
/// La couverture de persistance (vrai SQLite : incrément, décrément sûr, ajustement, rollback, motif) est portée
/// par <c>MMV.Application.Tests.UseCases.Stock.CreateStockMovementUseCaseTests</c>.
/// </summary>
public sealed class StockMovementFormViewModelTests
{
    /// <summary>Espion de use case : compte les appels, mémorise les commandes, joue un comportement.</summary>
    private sealed class SpyCreateStockMovementUseCase : ICreateStockMovementUseCase
    {
        private int _executeCount;
        private readonly Func<CreateStockMovementCommand, Task<CreateStockMovementResult>> _behavior;

        public SpyCreateStockMovementUseCase(Func<CreateStockMovementCommand, Task<CreateStockMovementResult>>? behavior = null)
            => _behavior = behavior ?? (cmd => Task.FromResult(SuccessResult(cmd)));

        public int ExecuteCount => _executeCount;
        public List<CreateStockMovementCommand> Commands { get; } = new();
        public CreateStockMovementCommand? LastCommand => Commands.Count > 0 ? Commands[^1] : null;

        public Task<CreateStockMovementResult> ExecuteAsync(CreateStockMovementCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executeCount);
            Commands.Add(command);
            return _behavior(command);
        }

        public static CreateStockMovementResult SuccessResult(CreateStockMovementCommand cmd) => new()
        {
            ProductFound = true,
            StockMovementId = 1,
            ProductId = cmd.ProductId,
            MovementType = cmd.MovementType,
            Quantity = cmd.Quantity,
            NewStockQuantity = cmd.Quantity,
        };
    }

    private sealed class FakeDialogService : IDialogService
    {
        public string? LastInfoMessage { get; private set; }

        public Task<bool> ShowConfirmationAsync(string title, string message) => Task.FromResult(false);

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

        public Task ShowInformationAsync(string title, string message)
        {
            LastInfoMessage = message;
            return Task.CompletedTask;
        }
    }

    private static StockMovementFormViewModel BuildViewModel(ICreateStockMovementUseCase useCase, IDialogService dialog)
    {
        var listProductsForPicker = new Mock<IListProductsForPickerUseCase>();
        return new StockMovementFormViewModel(listProductsForPicker.Object, dialog, useCase);
    }

    private static void AddLine(StockMovementFormViewModel viewModel, long productId, string movementType, int quantity, string notes = "")
    {
        var line = new ProductMovementLine(new List<ProductPickerItemDto> { new() { ProductId = productId } })
        {
            Product = new ProductPickerItemDto { ProductId = productId },
            MovementType = movementType,
            Quantity = quantity,
            Notes = notes,
        };
        viewModel.MovementLines.Add(line);
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

    // ------------------------------------------------------------------
    // (1) Une ligne valide : délégation + MovementSaved + mapping ligne → commande
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_ValidLine_DelegatesToUseCase_AndRaisesMovementSaved()
    {
        var spy = new SpyCreateStockMovementUseCase();
        var dialog = new FakeDialogService();
        var viewModel = BuildViewModel(spy, dialog);
        var saved = false;
        viewModel.MovementSaved += (_, _) => saved = true;

        AddLine(viewModel, productId: 7, movementType: "In", quantity: 4, notes: "Réception fournisseur");

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.True(saved);

        var command = spy.LastCommand!;
        Assert.Equal(7, command.ProductId);
        Assert.Equal(StockMovementType.In, command.MovementType);
        Assert.Equal(4, command.Quantity);
        Assert.Equal("Réception fournisseur", command.Reason);

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("1 mouvement(s) enregistré(s) avec succès.", dialog.LastInfoMessage);
    }

    // ------------------------------------------------------------------
    // (2) Motif par défaut « Mouvement {type} » quand aucune note n'est saisie
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WithoutNotes_BuildsDefaultReason()
    {
        var spy = new SpyCreateStockMovementUseCase();
        var dialog = new FakeDialogService();
        var viewModel = BuildViewModel(spy, dialog);

        AddLine(viewModel, productId: 3, movementType: "Out", quantity: 2);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal("Mouvement Out", spy.LastCommand!.Reason);
    }

    // ------------------------------------------------------------------
    // (3) Stock insuffisant propagé par le use case : message contrôlé, pas de MovementSaved
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenUseCaseThrowsInsufficientStock_ShowsControlledMessage_AndDoesNotRaiseSaved()
    {
        var spy = new SpyCreateStockMovementUseCase(_ => Task.FromException<CreateStockMovementResult>(
            new InsufficientStockException(productId: 7, requestedQuantity: 6, availableQuantity: 5)));
        var dialog = new FakeDialogService();
        var viewModel = BuildViewModel(spy, dialog);
        var saved = false;
        viewModel.MovementSaved += (_, _) => saved = true;

        AddLine(viewModel, productId: 7, movementType: "Out", quantity: 6);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
        Assert.Contains("Stock insuffisant", viewModel.ErrorMessage!);
        Assert.NotNull(dialog.LastInfoMessage);
        Assert.Contains("Aucun mouvement enregistré.", dialog.LastInfoMessage!);
    }

    // ------------------------------------------------------------------
    // (4) Produit introuvable : ligne comptée en erreur, pas de MovementSaved
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_WhenProductNotFound_CountsAsError_AndDoesNotRaiseSaved()
    {
        var spy = new SpyCreateStockMovementUseCase(_ => Task.FromResult(new CreateStockMovementResult { ProductFound = false }));
        var dialog = new FakeDialogService();
        var viewModel = BuildViewModel(spy, dialog);
        var saved = false;
        viewModel.MovementSaved += (_, _) => saved = true;

        AddLine(viewModel, productId: 99, movementType: "In", quantity: 1);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(1, spy.ExecuteCount);
        Assert.False(saved);
        Assert.Equal("Aucun mouvement enregistré.\n1 erreur(s) détectée(s).", dialog.LastInfoMessage);
    }

    // ------------------------------------------------------------------
    // (5) Plusieurs lignes valides : une délégation par ligne, message de synthèse
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_MultipleValidLines_DelegatesPerLine()
    {
        var spy = new SpyCreateStockMovementUseCase();
        var dialog = new FakeDialogService();
        var viewModel = BuildViewModel(spy, dialog);

        AddLine(viewModel, productId: 1, movementType: "In", quantity: 2);
        AddLine(viewModel, productId: 2, movementType: "Adjustment", quantity: 5);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.Equal(2, spy.ExecuteCount);
        Assert.Equal("2 mouvement(s) enregistré(s) avec succès.", dialog.LastInfoMessage);
    }

    // ------------------------------------------------------------------
    // (6) Aucune création possible sans use case (erreur de configuration)
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutCreateStockMovementUseCase_Throws()
    {
        var listProductsForPicker = new Mock<IListProductsForPickerUseCase>();
        var dialog = new FakeDialogService();

        Assert.Throws<ArgumentNullException>(() => new StockMovementFormViewModel(
            listProductsForPicker.Object, dialog, createStockMovementUseCase: null!));
    }

    // ------------------------------------------------------------------
    // (7) Garde IsSaving : remise à false en fin de flux
    // ------------------------------------------------------------------

    [Fact]
    public async Task Save_ResetsIsSaving_WhenComplete()
    {
        var spy = new SpyCreateStockMovementUseCase();
        var dialog = new FakeDialogService();
        var viewModel = BuildViewModel(spy, dialog);

        AddLine(viewModel, productId: 1, movementType: "In", quantity: 1);

        viewModel.SaveCommand.Execute(null);
        await WaitUntilAsync(() => !viewModel.IsSaving);

        Assert.False(viewModel.IsSaving);
    }
}
