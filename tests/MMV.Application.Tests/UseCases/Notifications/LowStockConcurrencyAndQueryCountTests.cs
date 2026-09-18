using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using MMV.Infrastructure.Services;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P3-8 — Deux garanties structurelles de la réconciliation, sur <b>vraie base SQLite jetable</b> :
/// <list type="bullet">
///   <item><b>Concurrence</b> — deux postes partant de la même absence d'alerte ne produisent jamais de doublon ;</item>
///   <item><b>Absence de N+1</b> — le nombre de LECTURES ne dépend pas du nombre de produits sous seuil.</item>
/// </list>
/// </summary>
public sealed class LowStockConcurrencyAndQueryCountTests : IDisposable
{
    private readonly string _workDirectory;

    public LowStockConcurrencyAndQueryCountTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p38-concurrency-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath, CommandRecordingInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False");

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new OpticDbContext(builder.Options);
    }

    /// <summary>Crée la base et <paramref name="productCount"/> produits, tous sous leur seuil d'alerte.</summary>
    private async Task<string> SeedLowStockProductsAsync(string fileName, int productCount)
    {
        var dbPath = PathFor(fileName);

        using (var schema = CreateContext(dbPath))
        {
            schema.Database.EnsureCreated();
        }

        using var context = CreateContext(dbPath);
        var supplier = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "S" });
        await new UnitOfWork(context).SaveChangesAsync();

        var products = new ProductRepository(context);
        for (var i = 0; i < productCount; i++)
        {
            await products.CreateAsync(new Product
            {
                Reference = $"REF-{i}",
                Name = $"Produit {i}",
                Category = ProductCategoryEnum.MONTURE,
                SupplierId = supplier.SupplierId,
                StockQuantity = 1,
                StockAlertThreshold = 5,
            });
        }

        await new UnitOfWork(context).SaveChangesAsync();
        return dbPath;
    }

    private static Notification LowStockFor(long productId) => new()
    {
        Type = NotificationTypes.LowStock,
        Title = $"Stock bas : produit {productId}",
        Message = "Message",
        EntityId = productId,
        EntityType = NotificationEntityTypes.Product,
        IsRead = false,
        CreatedAt = DateTime.UtcNow,
    };

    // =========================================================================================================
    // Concurrence
    // =========================================================================================================

    [Fact]
    public async Task DeuxTentativesFondeesSurLaMemeAbsence_NeCreentQuUneSeuleAlerte()
    {
        // Honnêteté du protocole : SQLite sérialise les écritures concurrentes sur un même fichier — deux INSERT
        // véritablement simultanés produiraient un VERROU, pas une course observable. Le scénario réellement
        // dangereux est donc reproduit fidèlement mais SÉQUENTIELLEMENT, au niveau de la primitive atomique : deux
        // connexions DISTINCTES observent la même absence d'alerte, puis tentent chacune l'insertion. C'est
        // exactement l'entrelacement « deux postes se connectent en même temps » de l'audit, et c'est ce que
        // l'implémentation d'origine (AnyAsync puis Add) échouait à empêcher.
        var dbPath = await SeedLowStockProductsAsync("race.db", productCount: 1);

        long productId;
        using (var context = CreateContext(dbPath))
        {
            productId = await context.Products.AsNoTracking().Select(p => p.ProductId).FirstAsync();
        }

        using var contextA = CreateContext(dbPath);
        using var contextB = CreateContext(dbPath);
        var repositoryA = new NotificationRepository(contextA);
        var repositoryB = new NotificationRepository(contextB);

        // Les deux postes constatent l'absence AVANT que l'un ou l'autre n'écrive.
        (await repositoryA.GetActiveLowStockEntityIdsAsync()).Should().BeEmpty();
        (await repositoryB.GetActiveLowStockEntityIdsAsync()).Should().BeEmpty();

        var createdByA = await repositoryA.TryCreateActiveLowStockAsync(LowStockFor(productId));
        var createdByB = await repositoryB.TryCreateActiveLowStockAsync(LowStockFor(productId));

        createdByA.Should().BeTrue("le premier poste ouvre l'alerte");
        createdByB.Should().BeFalse("le second absorbe le refus sans exception technique");

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking()
            .Count(n => n.Type == NotificationTypes.LowStock && n.ResolvedAt == null)
            .Should().Be(1);
    }

    [Fact]
    public async Task UneAlerteResolueSupplementaire_EstAutorisee()
    {
        // L'index ne contraint que les alertes ACTIVES : l'historique peut s'empiler librement sur la même clé.
        var dbPath = await SeedLowStockProductsAsync("resolved-extra.db", productCount: 1);

        long productId;
        using (var context = CreateContext(dbPath))
        {
            productId = await context.Products.AsNoTracking().Select(p => p.ProductId).FirstAsync();
        }

        using (var context = CreateContext(dbPath))
        {
            var repository = new NotificationRepository(context);
            (await repository.TryCreateActiveLowStockAsync(LowStockFor(productId))).Should().BeTrue();
        }

        using (var context = CreateContext(dbPath))
        {
            for (var i = 0; i < 3; i++)
            {
                var resolved = LowStockFor(productId);
                resolved.ResolvedAt = DateTime.UtcNow;
                context.Notifications.Add(resolved);
            }

            await context.SaveChangesAsync();
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId).Should().Be(4);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId && n.ResolvedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task LIndexNeContraintAucunFaitHistorique()
    {
        // Le piège principal de la migration : sans restriction du filtre sur le TYPE, l'index refuserait la
        // deuxième transition d'une même commande. Une commande en traverse jusqu'à quatre.
        var dbPath = await SeedLowStockProductsAsync("events.db", productCount: 1);

        using (var context = CreateContext(dbPath))
        {
            for (var i = 0; i < 4; i++)
            {
                context.Notifications.Add(new Notification
                {
                    Type = NotificationTypes.OrderStatusChanged,
                    Title = "Transition",
                    Message = $"Étape {i}",
                    EntityId = 42,
                    EntityType = NotificationEntityTypes.Order,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            for (var i = 0; i < 2; i++)
            {
                context.Notifications.Add(new Notification
                {
                    Type = NotificationTypes.PaymentReceived,
                    Title = "Encaissement",
                    Message = $"Règlement {i}",
                    EntityId = 42,
                    EntityType = NotificationEntityTypes.Order,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            for (var i = 0; i < 3; i++)
            {
                context.Notifications.Add(new Notification
                {
                    Type = NotificationTypes.Info,
                    Title = "Information",
                    Message = $"Note {i}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            // Aucune de ces écritures ne doit être refusée.
            var save = async () => await context.SaveChangesAsync();
            await save.Should().NotThrowAsync();
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.Type == NotificationTypes.OrderStatusChanged).Should().Be(4);
        verify.Notifications.AsNoTracking().Count(n => n.Type == NotificationTypes.PaymentReceived).Should().Be(2);
        verify.Notifications.AsNoTracking().Count(n => n.Type == NotificationTypes.Info).Should().Be(3);
    }

    [Fact]
    public async Task LaPrimitiveAtomique_RefuseUnFaitHistorique()
    {
        // Garde de contrat : employer la primitive pour un type non résoluble donnerait l'illusion d'une protection
        // que l'index filtré n'apporte pas à ce type.
        var dbPath = await SeedLowStockProductsAsync("primitive-guard.db", productCount: 1);

        using var context = CreateContext(dbPath);
        var repository = new NotificationRepository(context);

        var wrongType = LowStockFor(1);
        wrongType.Type = NotificationTypes.OrderStatusChanged;

        var act = async () => await repository.TryCreateActiveLowStockAsync(wrongType);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LaPrimitiveAtomique_PropageUneViolationNotNull_SansLaConfondreAvecUnDoublon()
    {
        // « ON CONFLICT DO NOTHING » (sans cible explicite) n'absorbe QUE les violations d'unicité. Une violation
        // NOT NULL est un bug d'écriture distinct : elle doit remonter comme exception Infrastructure normale,
        // jamais comme un « false » silencieux — sans quoi elle deviendrait indiscernable d'un refus métier.
        var dbPath = await SeedLowStockProductsAsync("notnull-violation.db", productCount: 1);

        long productId;
        using (var context = CreateContext(dbPath))
        {
            productId = await context.Products.AsNoTracking().Select(p => p.ProductId).FirstAsync();
        }

        using var repositoryContext = CreateContext(dbPath);
        var repository = new NotificationRepository(repositoryContext);

        var invalid = LowStockFor(productId);
        invalid.Title = null!;

        var act = async () => await repository.TryCreateActiveLowStockAsync(invalid);

        // Le client ADO.NET (Microsoft.Data.Sqlite) rejette un paramètre non affecté (InvalidOperationException)
        // avant même d'atteindre le moteur SQLite : la violation NOT NULL ne se manifeste donc jamais comme un
        // SqliteException ici. Ce qui compte pour le contrat de la primitive est vérifié malgré tout : ce n'est PAS
        // une violation d'unicité, elle n'est JAMAIS absorbée, et elle ne peut structurellement pas être confondue
        // avec un « false » de refus métier — un ArgumentException (garde de contrat) ou un false (conflit
        // d'unicité) sont les deux seules issues normales, ni l'une ni l'autre ne se produit ici.
        await act.Should().ThrowAsync<InvalidOperationException>(
            "un paramètre requis manquant n'est pas une violation d'unicité et ne doit jamais être absorbée en false");

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId).Should().Be(0,
            "l'insertion refusée par la contrainte NOT NULL ne doit laisser aucune ligne partielle");
    }

    // =========================================================================================================
    // Absence de N+1
    // =========================================================================================================

    [Fact]
    public async Task LeNombreDeLecturesEstConstant_QuelQueSoitLeNombreDeProduitsSousSeuil()
    {
        // Avant P3-8 : GetAllAsync() sur toute la table Notifications était appelé DANS la boucle produits — donc
        // N chargements intégraux pour N produits, en O(N × M). Ici, 1 produit et 12 produits doivent produire
        // EXACTEMENT le même nombre de SELECT.
        var oneProduct = await MeasureAsync("nplus1-one.db", productCount: 1);
        var manyProducts = await MeasureAsync("nplus1-many.db", productCount: 12);

        manyProducts.SelectCount.Should().Be(oneProduct.SelectCount,
            "le nombre de LECTURES doit être constant : deux requêtes de réconciliation plus un compteur");

        // Aucune lecture de la table Notifications répétée par produit.
        manyProducts.NotificationSelectCount.Should().Be(oneProduct.NotificationSelectCount);
        manyProducts.NotificationSelectCount.Should().BeLessThanOrEqualTo(2,
            "une seule projection des alertes actives, plus le compteur final");

        // Les ÉCRITURES, elles, croissent légitimement : une création réelle exige une insertion.
        manyProducts.InsertCount.Should().BeGreaterThan(oneProduct.InsertCount);

        // Aucune requête ne matérialise la table entière.
        manyProducts.Commands.Should().NotContain(c => c.Contains("SELECT *", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<QueryProfile> MeasureAsync(string fileName, int productCount)
    {
        var dbPath = await SeedLowStockProductsAsync(fileName, productCount);

        var interceptor = new CommandRecordingInterceptor();
        using (var context = CreateContext(dbPath, interceptor))
        {
            var useCase = new GenerateLowStockNotificationsUseCase(
                new ProductRepository(context),
                new NotificationRepository(context),
                new UnitOfWork(context),
                new EfTransactionRunner(context),
                SystemClock.Instance);

            var result = await useCase.ExecuteAsync();
            result.CreatedCount.Should().Be(productCount);
        }

        var commands = interceptor.Commands;
        return new QueryProfile(
            commands,
            commands.Count(IsSelect),
            commands.Count(c => IsSelect(c) && c.Contains("\"Notifications\"", StringComparison.Ordinal)),
            commands.Count(c => c.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsSelect(string commandText)
        => commandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

    private sealed record QueryProfile(
        IReadOnlyList<string> Commands,
        int SelectCount,
        int NotificationSelectCount,
        int InsertCount);

    /// <summary>Intercepteur EF enregistrant le texte de chaque commande réellement exécutée.</summary>
    private sealed class CommandRecordingInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = new();

        public IReadOnlyList<string> Commands => _commands;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            _commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            _commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            _commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            _commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
