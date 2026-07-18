using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Stock.CreateStockMovement;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Stock;

/// <summary>
/// P2B-2F — Quatrième vertical slice : use case « Créer un mouvement manuel de stock »
/// (<see cref="CreateStockMovementUseCase"/>), extrait iso-fonctionnellement de la boucle par ligne de
/// <c>StockMovementFormViewModel.SaveAsync</c>.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que le use case :
/// <list type="number">
///   <item>une entrée (In) incrémente le stock et enregistre le mouvement (motif préservé) ;</item>
///   <item>une sortie (Out) suffisante décrémente le stock via le décrément sûr et enregistre le mouvement ;</item>
///   <item>une sortie exactement égale au stock atteint zéro ;</item>
///   <item>une sortie supérieure au stock est <b>refusée</b> (InsufficientStockException), rien n'est persisté
///         (rollback transactionnel : stock inchangé, aucun mouvement), jamais de stock négatif ;</item>
///   <item>un ajustement (Adjustment) fixe le stock en valeur absolue et enregistre le mouvement ;</item>
///   <item>un produit introuvable renvoie <c>ProductFound = false</c> sans écrire ni lever d'exception ;</item>
///   <item>commande nulle : <see cref="ArgumentNullException"/> ;</item>
///   <item>dépendance critique manquante : le constructeur rejette <c>null</c>.</item>
/// </list>
/// Réutilise les mêmes repositories et ports P2A (<see cref="EfTransactionRunner"/>,
/// <see cref="EfStockMutationService"/>) que le flux d'origine, partageant un unique <see cref="OpticDbContext"/>.
/// </summary>
public sealed class CreateStockMovementUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public CreateStockMovementUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2b2f-" + Guid.NewGuid().ToString("N"));
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

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    // Vrai SQLite (fichier + schéma réels), jamais le provider InMemory. FK enforcées : un mouvement référence un
    // produit réel (et le produit un fournisseur réel), exactement comme le contexte de production du flux manuel.
    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Construit le use case réel au-dessus d'un unique contexte (DbContext partagé) et des ports P2A réels.</summary>
    private static CreateStockMovementUseCase CreateUseCase(OpticDbContext context)
    {
        return new CreateStockMovementUseCase(
            new StockMovementRepository(context),
            new ProductRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            new EfStockMutationService(context));
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedProduct(string databasePath, int initialStock)
    {
        using var context = CreateContext(databasePath);

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

    private static (int stock, int movements, StockMovement? movement) ReadState(string databasePath, long productId)
    {
        using var verify = CreateContext(databasePath);
        var stock = verify.Products.AsNoTracking().Single(p => p.ProductId == productId).StockQuantity;
        var movements = verify.StockMovements.AsNoTracking().Where(m => m.ProductId == productId).ToList();
        return (stock, movements.Count, movements.SingleOrDefault());
    }

    // ------------------------------------------------------------------
    // (1) Entrée (In) → incrément + mouvement (motif préservé)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_In_IncrementsStock_AndRecordsMovement()
    {
        var dbPath = PathFor("in.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 5);

        CreateStockMovementResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.In,
                Quantity = 4,
                Reason = "Réception fournisseur",
            });
        }

        result.ProductFound.Should().BeTrue();
        result.MovementType.Should().Be(StockMovementType.In);
        result.Quantity.Should().Be(4);
        result.NewStockQuantity.Should().Be(9);   // 5 + 4
        result.StockMovementId.Should().BeGreaterThan(0);

        var (stock, count, movement) = ReadState(dbPath, productId);
        stock.Should().Be(9);
        count.Should().Be(1);
        movement!.MovementType.Should().Be(StockMovementType.In);
        movement.Quantity.Should().Be(4, "la quantité du mouvement reste positive, comme le flux d'origine");
        movement.Reason.Should().Be("Réception fournisseur", "le motif est repris tel quel depuis la commande");
    }

    // ------------------------------------------------------------------
    // (2) Sortie (Out) suffisante → décrément sûr + mouvement
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Out_SufficientStock_DecrementsAndRecordsMovement()
    {
        var dbPath = PathFor("out-ok.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 10);

        CreateStockMovementResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.Out,
                Quantity = 3,
                Reason = "Sortie manuelle",
            });
        }

        result.ProductFound.Should().BeTrue();
        result.NewStockQuantity.Should().Be(7);   // 10 - 3

        var (stock, count, movement) = ReadState(dbPath, productId);
        stock.Should().Be(7);
        count.Should().Be(1);
        movement!.MovementType.Should().Be(StockMovementType.Out);
        movement.Quantity.Should().Be(-3, "sortie ⇒ delta négatif (convention de signe P3-5)");
        movement.Reason.Should().Be("Sortie manuelle");
    }

    // ------------------------------------------------------------------
    // (3) Sortie exactement égale au stock → 0
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Out_ExactlyAvailable_ReachesZero()
    {
        var dbPath = PathFor("out-exact.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.Out,
                Quantity = 5,
                Reason = "Sortie totale",
            });
        }

        var (stock, count, _) = ReadState(dbPath, productId);
        stock.Should().Be(0);
        count.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // (4) Sortie > stock → refus (InsufficientStockException), rollback total, jamais négatif
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Out_MoreThanAvailable_Throws_AndWritesNothing()
    {
        var dbPath = PathFor("out-over.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            Func<Task> act = () => useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.Out,
                Quantity = 6,  // 6 > 5
                Reason = "Sortie excessive",
            });

            await act.Should().ThrowAsync<InsufficientStockException>();
        }

        var (stock, count, _) = ReadState(dbPath, productId);
        stock.Should().Be(5, "rollback atomique : le stock reste inchangé");
        stock.Should().BeGreaterThanOrEqualTo(0, "le stock n'est jamais rendu négatif");
        count.Should().Be(0, "aucun mouvement n'est persisté en cas de refus (tout ou rien)");
    }

    // ------------------------------------------------------------------
    // (5) Ajustement (Adjustment) → valeur absolue + mouvement
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Adjustment_SetsAbsoluteStock_AndRecordsMovement()
    {
        var dbPath = PathFor("adjust.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 5);

        CreateStockMovementResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.Adjustment,
                Quantity = 8,
                Reason = "Inventaire",
            });
        }

        result.NewStockQuantity.Should().Be(8);   // valeur absolue

        var (stock, count, movement) = ReadState(dbPath, productId);
        stock.Should().Be(8, "l'ajustement fixe le stock en valeur absolue");
        count.Should().Be(1);
        movement!.MovementType.Should().Be(StockMovementType.Adjustment);
        movement.Reason.Should().Be("Inventaire");
    }

    // ------------------------------------------------------------------
    // (6) Produit introuvable → ProductFound = false, aucune écriture, aucune exception
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ProductNotFound_ReturnsNotFound_AndWritesNothing()
    {
        var dbPath = PathFor("notfound.db");
        EnsureSchema(dbPath);

        CreateStockMovementResult result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            result = await useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = 99999,
                MovementType = StockMovementType.In,
                Quantity = 1,
                Reason = "Mouvement In",
            });
        }

        result.ProductFound.Should().BeFalse();
        result.StockMovementId.Should().Be(0);

        using var verify = CreateContext(dbPath);
        verify.StockMovements.AsNoTracking().Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // (7) Commande nulle → ArgumentNullException
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NullCommand_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var useCase = CreateUseCase(context);

        Func<Task> act = () => useCase.ExecuteAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (8) Dépendance critique manquante → le constructeur rejette null
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_WithoutTransactionRunnerOrStockMutation_Throws()
    {
        var dbPath = PathFor("ctor.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);

        Action withoutRunner = () => _ = new CreateStockMovementUseCase(
            new StockMovementRepository(context),
            new ProductRepository(context),
            new UnitOfWork(context),
            transactionRunner: null!,
            new EfStockMutationService(context));
        withoutRunner.Should().Throw<ArgumentNullException>();

        Action withoutStockMutation = () => _ = new CreateStockMovementUseCase(
            new StockMovementRepository(context),
            new ProductRepository(context),
            new UnitOfWork(context),
            new EfTransactionRunner(context),
            stockMutationService: null!);
        withoutStockMutation.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // (9) P3-5 — Ajustement : le mouvement enregistre le DELTA SIGNÉ (nouvelle − ancienne), pas la valeur absolue
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Adjustment_RecordsSignedDelta_NotAbsoluteValue()
    {
        var dbPath = PathFor("adjust-delta.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            var useCase = CreateUseCase(context);
            await useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.Adjustment,
                Quantity = 2,   // cible absolue 2, en partant de 5 ⇒ delta = -3
                Reason = "Inventaire",
            });
        }

        var (stock, count, movement) = ReadState(dbPath, productId);
        stock.Should().Be(2, "l'ajustement fixe le stock à la valeur comptée");
        count.Should().Be(1);
        movement!.MovementType.Should().Be(StockMovementType.Adjustment);
        movement.Quantity.Should().Be(-3, "delta signé = nouvelle (2) − ancienne (5), convention P3-5");
    }

    // ------------------------------------------------------------------
    // (10) P3-5 — Conflit d'ajustement concurrent : rollback complet, AUCUN mouvement conservé
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_Adjustment_ConcurrencyConflict_RollsBack_NoMovementKept()
    {
        var dbPath = PathFor("adjust-conflict.db");
        EnsureSchema(dbPath);
        var productId = SeedProduct(dbPath, initialStock: 5);

        using (var context = CreateContext(dbPath))
        {
            // Service dont l'ajustement lève un conflit contrôlé (un autre poste a modifié le stock entre-temps).
            var useCase = new CreateStockMovementUseCase(
                new StockMovementRepository(context),
                new ProductRepository(context),
                new UnitOfWork(context),
                new EfTransactionRunner(context),
                new ConflictingAdjustStockMutationService(productId, expectedQuantity: 5));

            Func<Task> act = () => useCase.ExecuteAsync(new CreateStockMovementCommand
            {
                ProductId = productId,
                MovementType = StockMovementType.Adjustment,
                Quantity = 9,
                Reason = "Inventaire concurrent",
            });

            await act.Should().ThrowAsync<StockConcurrencyConflictException>();
        }

        var (stock, count, _) = ReadState(dbPath, productId);
        stock.Should().Be(5, "rollback : le stock reste inchangé après conflit");
        count.Should().Be(0, "aucun mouvement n'est conservé quand la concurrence invalide l'état lu (tout ou rien)");
    }

    /// <summary>
    /// Service de mutation dont seule la branche ajustement lève un <see cref="StockConcurrencyConflictException"/>
    /// contrôlé — simule un autre poste ayant modifié le stock entre la lecture et l'écriture.
    /// </summary>
    private sealed class ConflictingAdjustStockMutationService : IStockMutationService
    {
        private readonly long _productId;
        private readonly int _expectedQuantity;

        public ConflictingAdjustStockMutationService(long productId, int expectedQuantity)
        {
            _productId = productId;
            _expectedQuantity = expectedQuantity;
        }

        public Task DecrementStockAsync(long productId, int quantity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> IncrementStockAsync(long productId, int quantity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StockAdjustmentResult> AdjustStockToAsync(long productId, int targetQuantity, CancellationToken cancellationToken = default)
            => throw new StockConcurrencyConflictException(_productId, _expectedQuantity);
    }
}
