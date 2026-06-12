using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Domain.Tests.Persistence;

/// <summary>
/// P2A-1C — Frontière transactionnelle, idempotence et erreurs de persistance.
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que
/// <see cref="EfTransactionRunner"/> :
/// <list type="number">
///   <item>valide une opération multi-écriture réussie (toutes les écritures persistées) ;</item>
///   <item>annule TOUT en cas d'exception au milieu (aucune écriture partielle) ;</item>
///   <item>transforme une <c>DbUpdateException</c>/<c>SqliteException</c> en
///         <see cref="PersistenceException"/> contrôlée et annule l'opération ;</item>
///   <item>propage inchangées les exceptions non liées à la persistance (tout en annulant l'écriture) ;</item>
///   <item>se rattache à une transaction déjà ouverte (frontière imbriquée) sans en ouvrir une seconde.</item>
/// </list>
/// Chaque test utilise un fichier SQLite temporaire isolé : la vérification « rien n'a été persisté »
/// est faite via une connexion/un contexte distinct (lecture de l'état réellement validé).
/// </summary>
public sealed class EfTransactionRunnerTests : IDisposable
{
    private readonly string _workDirectory;

    public EfTransactionRunnerTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2a1c-" + Guid.NewGuid().ToString("N"));
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

    private static OpticDbContext CreateContext(string databasePath)
    {
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    /// <summary>Crée la base et y insère un client (FK), renvoie son identifiant.</summary>
    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
        var customer = new Customer { FirstName = "Trans", LastName = "Action" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static Sale NewSale(long customerId, string saleNumber) => new()
    {
        CustomerId = customerId,
        SaleNumber = saleNumber,
        SaleDate = DateTime.UtcNow,
        TotalAmount = 100m,
        FinalAmount = 100m,
        PaymentMethod = PaymentMethod.Cash,
        Status = SaleStatus.Delivered,
    };

    // ------------------------------------------------------------------
    // (1) Opération multi-écriture réussie → tout est persisté
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_MultiWriteSuccess_CommitsAllWrites()
    {
        var dbPath = PathFor("success.db");
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);

            await runner.RunAsync(async ct =>
            {
                // 1re écriture
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-OK-1"), ct);
                await unitOfWork.SaveChangesAsync(ct);

                // 2e écriture (même transaction)
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-OK-2"), ct);
                await unitOfWork.SaveChangesAsync(ct);
            });
        }

        // Vérification via un contexte neuf (état réellement validé)
        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(2, "l'opération multi-écriture a été validée intégralement");
    }

    // ------------------------------------------------------------------
    // (2) Exception au milieu → rollback complet, aucune écriture partielle
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_ExceptionAfterFirstSave_RollsBackEverything()
    {
        var dbPath = PathFor("rollback.db");
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);

            Func<Task> act = () => runner.RunAsync(async ct =>
            {
                // 1re écriture RÉUSSIE (validée seulement au commit)
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-PARTIAL"), ct);
                await unitOfWork.SaveChangesAsync(ct);

                // Panne au milieu, AVANT la 2e écriture
                throw new InvalidOperationException("panne métier au milieu de l'opération");
            });

            // Exception non liée à la persistance → propagée inchangée
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0,
            "la transaction a été annulée : la vente écrite par le 1er SaveChanges ne doit pas subsister");
    }

    // ------------------------------------------------------------------
    // (3) DbUpdateException (violation d'unicité) → PersistenceException + rollback
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_UniqueConstraintViolation_ThrowsControlledPersistenceException_AndRollsBack()
    {
        var dbPath = PathFor("unique.db");
        var customerId = SeedCustomer(dbPath);

        // Une vente "VTE-DUP" existe déjà (validée hors runner).
        using (var seed = CreateContext(dbPath))
        {
            seed.Sales.Add(NewSale(customerId, "VTE-DUP"));
            seed.SaveChanges();
        }

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);

            Func<Task> act = () => runner.RunAsync(async ct =>
            {
                // Écriture bénigne d'abord (doit être annulée par le rollback)
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-BENIGNE"), ct);
                await unitOfWork.SaveChangesAsync(ct);

                // Doublon de SaleNumber → index UNIQUE idx_sales_sale_number_unique → DbUpdateException
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-DUP"), ct);
                await unitOfWork.SaveChangesAsync(ct);
            });

            var assertion = await act.Should().ThrowAsync<PersistenceException>();
            assertion.Which.Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
            assertion.Which.Message.Should().NotBeNullOrWhiteSpace();
            assertion.Which.InnerException.Should().NotBeNull("le détail technique reste disponible pour le journal");
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count(s => s.SaleNumber == "VTE-DUP").Should().Be(1, "le doublon a été rejeté");
        verify.Sales.Any(s => s.SaleNumber == "VTE-BENIGNE").Should().BeFalse(
            "l'écriture bénigne du 1er SaveChanges a été annulée par le rollback (aucune écriture partielle)");
    }

    // ------------------------------------------------------------------
    // (4) Exception générique (non persistance) → propagée inchangée + rollback
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NonPersistenceException_IsRethrownUnchanged_AndRollsBack()
    {
        var dbPath = PathFor("generic.db");
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);

            Func<Task> act = () => runner.RunAsync(async ct =>
            {
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-GEN"), ct);
                await unitOfWork.SaveChangesAsync(ct);
                throw new ArgumentException("erreur applicative non liée à la persistance");
            });

            // Non transformée en PersistenceException
            await act.Should().ThrowAsync<ArgumentException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "rollback complet malgré une exception non-persistance");
    }

    // ------------------------------------------------------------------
    // (5) Frontière imbriquée → se rattache à la transaction existante
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_NestedWithinExistingTransaction_DoesNotOpenSecondTransaction()
    {
        var dbPath = PathFor("nested.db");
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var unitOfWork = new UnitOfWork(context);
            var runner = new EfTransactionRunner(context);

            await using var outer = await context.Database.BeginTransactionAsync();

            // Le runner doit détecter la transaction ouverte et s'y rattacher (pas de 2nde transaction).
            await runner.RunAsync(async ct =>
            {
                await unitOfWork.Sales.CreateAsync(NewSale(customerId, "VTE-NESTED"), ct);
                await unitOfWork.SaveChangesAsync(ct);
            });

            // Tant que la transaction externe n'est pas validée, rien n'est durable :
            // l'annuler doit faire disparaître l'écriture du runner imbriqué.
            await outer.RollbackAsync();
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0,
            "le runner s'est rattaché à la transaction externe ; son rollback annule aussi l'écriture imbriquée");
    }

    // ------------------------------------------------------------------
    // (6) PersistenceErrorMapper — classification & neutralité
    // ------------------------------------------------------------------

    [Fact]
    public void Mapper_MapsSqliteUniqueException_ToUniqueConstraintCategory()
    {
        var sqlite = new SqliteException("UNIQUE constraint failed", 19, 2067);

        var mapped = PersistenceErrorMapper.Map(sqlite);

        mapped.Should().BeOfType<PersistenceException>();
        ((PersistenceException)mapped).Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
        mapped.InnerException.Should().BeSameAs(sqlite);
    }

    [Fact]
    public void Mapper_MapsSqliteBusyException_ToDatabaseBusyCategory()
    {
        var sqlite = new SqliteException("database is locked", 5, 5);

        var mapped = PersistenceErrorMapper.Map(sqlite);

        ((PersistenceException)mapped).Category.Should().Be(PersistenceErrorCategory.DatabaseBusy);
    }

    [Fact]
    public void Mapper_MapsDbUpdateExceptionWithSqliteInner_ToPersistenceException()
    {
        var sqlite = new SqliteException("UNIQUE constraint failed", 19, 2067);
        var dbUpdate = new DbUpdateException("update failed", sqlite);

        var mapped = PersistenceErrorMapper.Map(dbUpdate);

        mapped.Should().BeOfType<PersistenceException>();
        ((PersistenceException)mapped).Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
    }

    [Fact]
    public void Mapper_LeavesNonPersistenceException_Unchanged()
    {
        var business = new InvalidOperationException("règle métier");

        var mapped = PersistenceErrorMapper.Map(business);

        mapped.Should().BeSameAs(business, "une exception non liée à la persistance n'est pas transformée");
    }
}
