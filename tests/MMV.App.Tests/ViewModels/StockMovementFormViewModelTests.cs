using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.App.Services;
using MMV.App.ViewModels;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.App.Tests.ViewModels;

/// <summary>
/// P2A-1D-R2 — Sécurisation des sorties manuelles de stock (<see cref="StockMovementFormViewModel"/>).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b>, qu'une sortie manuelle standard :
/// <list type="number">
///   <item>décrémente le stock quand la quantité est suffisante ;</item>
///   <item>autorise un décrément exactement égal au stock disponible (→ 0) ;</item>
///   <item>est <b>refusée</b> (erreur contrôlée) quand la quantité dépasse le stock ;</item>
///   <item>ne rend <b>jamais</b> le stock négatif ;</item>
///   <item>ne persiste <b>aucun</b> <see cref="StockMovement"/> en cas de refus ;</item>
///   <item>décrément + mouvement sont <b>atomiques</b> (tout ou rien) ;</item>
///   <item>affiche un <b>message utilisateur contrôlé</b> et aucune notification de succès ;</item>
///   <item>n'utilise <b>plus</b> de confirmation autorisant volontairement un stock négatif.</item>
/// </list>
/// Une entrée (In) reste un incrément ; un ajustement (Adjustment) reste une correction absolue (≥ 0).
/// </summary>
public sealed class StockMovementFormViewModelTests : IDisposable
{
    private readonly string _workDirectory;

    public StockMovementFormViewModelTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2a1d-r2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }

    // ------------------------------------------------------------------
    // Doubles & utilitaires
    // ------------------------------------------------------------------

    private sealed class FakeDialogService : IDialogService
    {
        public string? LastInfoMessage { get; private set; }
        public int ConfirmationCount { get; private set; }

        public Task<bool> ShowConfirmationAsync(string title, string message)
        {
            ConfirmationCount++;
            return Task.FromResult(false);
        }

        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;

        public Task ShowInformationAsync(string title, string message)
        {
            LastInfoMessage = message;
            return Task.CompletedTask;
        }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    private static long SeedProduct(string databasePath, int initialStock)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();

        var supplier = new Supplier { Name = "Fournisseur Test" };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = "REF-MAN-001",
            Name = "Monture Test",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = initialStock,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    private static StockMovementFormViewModel BuildViewModel(OpticDbContext context, IDialogService dialog)
    {
        var unitOfWork = new UnitOfWork(context);
        var productRepository = new ProductRepository(context);
        var stockMovementRepository = new StockMovementRepository(context);
        var runner = new EfTransactionRunner(context);
        var stockMutation = new EfStockMutationService(context);

        return new StockMovementFormViewModel(
            stockMovementRepository, productRepository, unitOfWork, dialog, runner, stockMutation);
    }

    private static void AddLine(StockMovementFormViewModel viewModel, long productId, string movementType, int quantity)
    {
        var line = new ProductMovementLine(new List<Product> { new() { ProductId = productId } })
        {
            Product = new Product { ProductId = productId },
            MovementType = movementType,
            Quantity = quantity,
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

    private static (int stock, int movements) ReadState(string databasePath, long productId)
    {
        using var verify = CreateContext(databasePath);
        var stock = verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity;
        var movements = verify.StockMovements.AsNoTracking().Count(m => m.ProductId == productId);
        return (stock, movements);
    }

    // ------------------------------------------------------------------
    // (1) Sortie suffisante → décrément + mouvement persistés
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManualOut_SufficientStock_DecrementsAndRecordsMovement()
    {
        var dbPath = PathFor("man-out-ok.db");
        var productId = SeedProduct(dbPath, initialStock: 10);
        var dialog = new FakeDialogService();
        var saved = false;

        using (var context = CreateContext(dbPath))
        {
            var viewModel = BuildViewModel(context, dialog);
            viewModel.MovementSaved += (_, _) => saved = true;
            AddLine(viewModel, productId, "Out", 3);

            viewModel.SaveCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsSaving);
        }

        var (stock, movements) = ReadState(dbPath, productId);
        Assert.Equal(7, stock);            // 10 - 3
        Assert.Equal(1, movements);        // mouvement enregistré
        Assert.True(saved);
        Assert.Equal(0, dialog.ConfirmationCount); // plus aucune confirmation de stock négatif
    }

    // ------------------------------------------------------------------
    // (2) Sortie exactement égale au stock → 0
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManualOut_ExactlyAvailable_ReachesZero()
    {
        var dbPath = PathFor("man-out-exact.db");
        var productId = SeedProduct(dbPath, initialStock: 5);
        var dialog = new FakeDialogService();

        using (var context = CreateContext(dbPath))
        {
            var viewModel = BuildViewModel(context, dialog);
            AddLine(viewModel, productId, "Out", 5);

            viewModel.SaveCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsSaving);
        }

        var (stock, movements) = ReadState(dbPath, productId);
        Assert.Equal(0, stock);
        Assert.Equal(1, movements);
    }

    // ------------------------------------------------------------------
    // (3-7) Sortie supérieure au stock → refus contrôlé, rien persisté, jamais négatif
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManualOut_MoreThanAvailable_IsRefused_NoMovement_NoNegative_ControlledMessage()
    {
        var dbPath = PathFor("man-out-over.db");
        var productId = SeedProduct(dbPath, initialStock: 5);
        var dialog = new FakeDialogService();
        var saved = false;

        StockMovementFormViewModel viewModel;
        using (var context = CreateContext(dbPath))
        {
            viewModel = BuildViewModel(context, dialog);
            viewModel.MovementSaved += (_, _) => saved = true;
            AddLine(viewModel, productId, "Out", 6); // 6 > 5

            viewModel.SaveCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsSaving);
        }

        var (stock, movements) = ReadState(dbPath, productId);
        Assert.Equal(5, stock);                     // (4) inchangé, jamais négatif
        Assert.True(stock >= 0);
        Assert.Equal(0, movements);                 // (5/6) atomique : aucun mouvement persisté
        Assert.False(saved);                        // (7) pas de notification de succès
        Assert.False(string.IsNullOrWhiteSpace(viewModel.ErrorMessage)); // message contrôlé exposé
        Assert.Contains("Stock insuffisant", viewModel.ErrorMessage!);
        Assert.Equal(0, dialog.ConfirmationCount);  // (8) plus de confirmation de stock négatif
    }

    // ------------------------------------------------------------------
    // (Comportement) Entrée → incrément (inchangé, jamais négatif)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManualIn_IncrementsStock_AndRecordsMovement()
    {
        var dbPath = PathFor("man-in.db");
        var productId = SeedProduct(dbPath, initialStock: 5);
        var dialog = new FakeDialogService();

        using (var context = CreateContext(dbPath))
        {
            var viewModel = BuildViewModel(context, dialog);
            AddLine(viewModel, productId, "In", 4);

            viewModel.SaveCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsSaving);
        }

        var (stock, movements) = ReadState(dbPath, productId);
        Assert.Equal(9, stock);     // 5 + 4
        Assert.Equal(1, movements);
    }

    // ------------------------------------------------------------------
    // (Comportement) Ajustement → correction absolue contrôlée (≥ 0)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ManualAdjustment_SetsAbsoluteStock_AndRecordsMovement()
    {
        var dbPath = PathFor("man-adjust.db");
        var productId = SeedProduct(dbPath, initialStock: 5);
        var dialog = new FakeDialogService();

        using (var context = CreateContext(dbPath))
        {
            var viewModel = BuildViewModel(context, dialog);
            AddLine(viewModel, productId, "Adjustment", 8);

            viewModel.SaveCommand.Execute(null);
            await WaitUntilAsync(() => !viewModel.IsSaving);
        }

        var (stock, movements) = ReadState(dbPath, productId);
        Assert.Equal(8, stock);     // ajustement absolu
        Assert.Equal(1, movements);
        Assert.True(stock >= 0);
    }

    // ------------------------------------------------------------------
    // (R2) Construction impossible sans frontière transactionnelle / décrément sûr
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutTransactionRunnerOrStockMutation_Throws()
    {
        var dbPath = PathFor("ctor-guard.db");
        SeedProduct(dbPath, initialStock: 1);
        var dialog = new FakeDialogService();

        using var context = CreateContext(dbPath);
        var unitOfWork = new UnitOfWork(context);
        var productRepository = new ProductRepository(context);
        var stockMovementRepository = new StockMovementRepository(context);
        var stockMutation = new EfStockMutationService(context);

        Assert.Throws<ArgumentNullException>(() => new StockMovementFormViewModel(
            stockMovementRepository, productRepository, unitOfWork, dialog, null!, stockMutation));

        Assert.Throws<ArgumentNullException>(() => new StockMovementFormViewModel(
            stockMovementRepository, productRepository, unitOfWork, dialog, new EfTransactionRunner(context), null!));
    }
}
