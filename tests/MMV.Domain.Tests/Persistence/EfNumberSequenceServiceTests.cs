using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Domain.Tests.Persistence;

/// <summary>
/// P2A-1E — Numérotation fiable des ventes et commandes (R-03 / ADR-006).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b> (jamais le provider InMemory), que
/// <see cref="EfNumberSequenceService"/> et son intégration dans la frontière transactionnelle
/// (<see cref="EfTransactionRunner"/>) :
/// <list type="number">
///   <item>attribuent le premier numéro de vente (<c>VTE-000001</c>) ;</item>
///   <item>attribuent le deuxième numéro de vente (<c>VTE-000002</c>) ;</item>
///   <item>attribuent le premier numéro de commande (<c>CMD-000001</c>) ;</item>
///   <item>traitent les séquences <c>SALE</c> et <c>ORDER</c> de façon indépendante ;</item>
///   <item>ne produisent <b>aucune collision</b> sur des appels successifs ;</item>
///   <item>sous appels <b>concurrents</b>, attribuent des numéros tous uniques (aucun doublon) ;</item>
///   <item>rollback : un numéro attribué dans une transaction annulée n'est <b>pas</b> consommé ;
///         un numéro attribué dans une transaction validée l'est ;</item>
///   <item>hors transaction, le numéro est consommé dès l'attribution (comportement retenu, trou possible) ;</item>
///   <item>séquence introuvable / nom vide ⇒ erreur contrôlée ;</item>
///   <item>les numéros générés respectent l'index UNIQUE <c>SaleNumber</c> (insertion réelle de 50 ventes).</item>
/// </list>
/// Chaque test utilise un fichier SQLite temporaire isolé ; l'état réellement validé est relu via un
/// contexte distinct. Les compteurs <c>SALE</c>/<c>ORDER</c> sont seedés par <c>EnsureCreated</c> (HasData).
/// </summary>
public sealed class EfNumberSequenceServiceTests : IDisposable
{
    private readonly string _workDirectory;

    public EfNumberSequenceServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2a1e-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>Crée la base (le schéma + le seed HasData des séquences SALE/ORDER).</summary>
    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static long SeedCustomer(string databasePath)
    {
        using var context = CreateContext(databasePath);
        var customer = new Customer { FirstName = "Num", LastName = "Client" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    private static long ReadCurrentValue(string databasePath, string sequenceName)
    {
        using var verify = CreateContext(databasePath);
        return verify.DocumentSequences.AsNoTracking().Single(s => s.SequenceName == sequenceName).CurrentValue;
    }

    // ------------------------------------------------------------------
    // (1)(2) Premier puis deuxième numéro de vente
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_Sale_FirstThenSecond_AreSequential()
    {
        var dbPath = PathFor("sale-sequence.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var service = new EfNumberSequenceService(context);

        var first = await service.NextNumberAsync(DocumentSequenceNames.Sale);
        var second = await service.NextNumberAsync(DocumentSequenceNames.Sale);

        first.Should().Be("VTE-000001", "le compteur démarre à 0, le premier numéro émis est 1");
        second.Should().Be("VTE-000002", "le numéro suivant est strictement incrémenté");
        ReadCurrentValue(dbPath, DocumentSequenceNames.Sale).Should().Be(2);
    }

    // ------------------------------------------------------------------
    // (3) Premier numéro de commande
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_Order_First_IsCmd000001()
    {
        var dbPath = PathFor("order-sequence.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var service = new EfNumberSequenceService(context);

        var first = await service.NextNumberAsync(DocumentSequenceNames.Order);

        first.Should().Be("CMD-000001");
    }

    // ------------------------------------------------------------------
    // (4) Les séquences SALE et ORDER sont indépendantes
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_SaleAndOrder_AreIndependentSequences()
    {
        var dbPath = PathFor("independent.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var service = new EfNumberSequenceService(context);

        var sale1 = await service.NextNumberAsync(DocumentSequenceNames.Sale);
        var order1 = await service.NextNumberAsync(DocumentSequenceNames.Order);
        var sale2 = await service.NextNumberAsync(DocumentSequenceNames.Sale);
        var order2 = await service.NextNumberAsync(DocumentSequenceNames.Order);

        sale1.Should().Be("VTE-000001");
        sale2.Should().Be("VTE-000002");
        order1.Should().Be("CMD-000001", "la séquence ORDER n'est pas affectée par les attributions SALE");
        order2.Should().Be("CMD-000002");
    }

    // ------------------------------------------------------------------
    // (5) Aucune collision sur des appels successifs
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_ManySuccessiveCalls_AreAllDistinct()
    {
        var dbPath = PathFor("successive.db");
        EnsureSchema(dbPath);

        var generated = new List<string>();
        using (var context = CreateContext(dbPath))
        {
            var service = new EfNumberSequenceService(context);
            for (var i = 0; i < 50; i++)
            {
                generated.Add(await service.NextNumberAsync(DocumentSequenceNames.Sale));
            }
        }

        generated.Should().OnlyHaveUniqueItems("aucun numéro ne doit être attribué deux fois");
        generated.Should().HaveCount(50);
        generated[0].Should().Be("VTE-000001");
        generated[49].Should().Be("VTE-000050");
    }

    // ------------------------------------------------------------------
    // (6) Concurrence : plusieurs appels simultanés → numéros tous uniques
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_ConcurrentCalls_ProduceUniqueNumbers()
    {
        var dbPath = PathFor("concurrent.db");
        EnsureSchema(dbPath);

        const int parallelism = 10;

        async Task<string> AllocateAsync()
        {
            using var context = CreateContext(dbPath); // connexion/contexte propre (Pooling=False)
            var service = new EfNumberSequenceService(context);
            return await service.NextNumberAsync(DocumentSequenceNames.Sale);
        }

        var tasks = Enumerable.Range(0, parallelism).Select(_ => AllocateAsync());
        var results = await Task.WhenAll(tasks);

        results.Should().OnlyHaveUniqueItems("aucun numéro ne peut être attribué deux fois, même sous concurrence");
        results.Should().HaveCount(parallelism);
        ReadCurrentValue(dbPath, DocumentSequenceNames.Sale).Should().Be(parallelism,
            "exactement N attributions ont eu lieu, sans perte ni doublon");
    }

    // ------------------------------------------------------------------
    // (7) Rollback : numéro NON consommé si la transaction est annulée
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_WithinRolledBackTransaction_DoesNotConsumeNumber()
    {
        var dbPath = PathFor("rollback.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var runner = new EfTransactionRunner(context);
            var unitOfWork = new UnitOfWork(context);
            var numbers = new EfNumberSequenceService(context);

            Func<Task> act = () => runner.RunAsync(async ct =>
            {
                // 1) Numéro attribué DANS la transaction (compteur passe à 1)…
                var saleNumber = await numbers.NextNumberAsync(DocumentSequenceNames.Sale, ct);
                await unitOfWork.Sales.CreateAsync(new Sale
                {
                    CustomerId = customerId,
                    SaleNumber = saleNumber,
                    SaleDate = DateTime.UtcNow,
                    TotalAmount = 10m,
                    FinalAmount = 10m,
                    PaymentMethod = PaymentMethod.Cash,
                    Status = SaleStatus.Delivered,
                }, ct);
                await unitOfWork.SaveChangesAsync(ct);

                // 2) …puis la vente échoue → la transaction est annulée.
                throw new InvalidOperationException("échec simulé après attribution du numéro");
            });

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        ReadCurrentValue(dbPath, DocumentSequenceNames.Sale).Should().Be(0,
            "l'incrément du compteur est annulé avec la vente : le numéro n'est pas consommé");
        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(0, "la vente annulée n'est pas persistée");
    }

    [Fact]
    public async Task NextNumberAsync_WithinCommittedTransaction_ConsumesNumber()
    {
        var dbPath = PathFor("commit.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        string? saleNumber = null;
        using (var context = CreateContext(dbPath))
        {
            var runner = new EfTransactionRunner(context);
            var unitOfWork = new UnitOfWork(context);
            var numbers = new EfNumberSequenceService(context);

            await runner.RunAsync(async ct =>
            {
                saleNumber = await numbers.NextNumberAsync(DocumentSequenceNames.Sale, ct);
                await unitOfWork.Sales.CreateAsync(new Sale
                {
                    CustomerId = customerId,
                    SaleNumber = saleNumber,
                    SaleDate = DateTime.UtcNow,
                    TotalAmount = 10m,
                    FinalAmount = 10m,
                    PaymentMethod = PaymentMethod.Cash,
                    Status = SaleStatus.Delivered,
                }, ct);
                await unitOfWork.SaveChangesAsync(ct);
            });
        }

        saleNumber.Should().Be("VTE-000001");
        ReadCurrentValue(dbPath, DocumentSequenceNames.Sale).Should().Be(1, "la vente validée consomme le numéro");
        using var verify = CreateContext(dbPath);
        verify.Sales.Single().SaleNumber.Should().Be("VTE-000001");
    }

    // ------------------------------------------------------------------
    // (8) Hors transaction : le numéro est consommé dès l'attribution (comportement retenu)
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_OutsideTransaction_ConsumesEvenIfCallerDiscards()
    {
        var dbPath = PathFor("autocommit.db");
        EnsureSchema(dbPath);

        // Première attribution (ex. ouverture d'un formulaire de commande), puis le caller "abandonne".
        using (var context = CreateContext(dbPath))
        {
            var service = new EfNumberSequenceService(context);
            (await service.NextNumberAsync(DocumentSequenceNames.Order)).Should().Be("CMD-000001");
        }

        // Une seconde attribution obtient le numéro SUIVANT : l'abandon laisse un trou (numérotation non fiscale).
        using (var context = CreateContext(dbPath))
        {
            var service = new EfNumberSequenceService(context);
            (await service.NextNumberAsync(DocumentSequenceNames.Order)).Should().Be("CMD-000002");
        }
    }

    // ------------------------------------------------------------------
    // (9) Erreurs contrôlées : séquence introuvable / nom vide
    // ------------------------------------------------------------------

    [Fact]
    public async Task NextNumberAsync_UnknownSequence_ThrowsControlled()
    {
        var dbPath = PathFor("unknown.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var service = new EfNumberSequenceService(context);

        Func<Task> act = () => service.NextNumberAsync("DOES-NOT-EXIST");

        var assertion = await act.Should().ThrowAsync<NumberSequenceException>();
        assertion.Which.SequenceName.Should().Be("DOES-NOT-EXIST");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NextNumberAsync_BlankSequenceName_ThrowsArgumentException(string sequenceName)
    {
        var dbPath = PathFor($"blank-{sequenceName.Length}.db");
        EnsureSchema(dbPath);

        using var context = CreateContext(dbPath);
        var service = new EfNumberSequenceService(context);

        Func<Task> act = () => service.NextNumberAsync(sequenceName);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ------------------------------------------------------------------
    // (10) Les numéros générés respectent l'index UNIQUE SaleNumber (insertion réelle)
    // ------------------------------------------------------------------

    [Fact]
    public async Task GeneratedSaleNumbers_RespectUniqueIndex_When50SalesArePersisted()
    {
        var dbPath = PathFor("unique-index.db");
        EnsureSchema(dbPath);
        var customerId = SeedCustomer(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var service = new EfNumberSequenceService(context);
            for (var i = 0; i < 50; i++)
            {
                var saleNumber = await service.NextNumberAsync(DocumentSequenceNames.Sale);
                context.Sales.Add(new Sale
                {
                    CustomerId = customerId,
                    SaleNumber = saleNumber,
                    SaleDate = DateTime.UtcNow,
                    TotalAmount = 1m,
                    FinalAmount = 1m,
                    PaymentMethod = PaymentMethod.Cash,
                    Status = SaleStatus.Delivered,
                });
                await context.SaveChangesAsync(); // l'index UNIQUE idx_sales_sale_number_unique rejetterait un doublon
            }
        }

        using var verify = CreateContext(dbPath);
        verify.Sales.Count().Should().Be(50, "50 ventes aux numéros uniques sont persistées sans violation d'unicité");
        verify.Sales.AsNoTracking().Select(s => s.SaleNumber).Distinct().Count().Should().Be(50);
    }
}
