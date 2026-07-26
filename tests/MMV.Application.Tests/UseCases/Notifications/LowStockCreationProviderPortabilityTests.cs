using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Notifications;

/// <summary>
/// P4-1 Lot D — <b>piste SQLite</b> de la portabilité de <c>NotificationRepository.TryCreateActiveLowStockAsync</c>.
///
/// <para>
/// La primitive fabriquait ses paramètres en <c>SqliteParameter</c> <b>en dur</b>, ce qui la rendait inutilisable
/// dès qu'un autre provider était monté (rejet à l'ajout dans la collection de la commande, avant même le moteur).
/// Le Lot D remplace ce couplage par une fabrication de paramètres <b>déléguée au provider courant</b> et par un
/// dialecte choisi selon ce provider.
/// </para>
///
/// <para>
/// Ces tests-ci vérifient que la piste SQLite — la seule montée en production aujourd'hui — conserve
/// <b>exactement</b> ses garanties : décision prise par la base, en <b>une seule instruction</b>, sans lecture
/// préalable, et jeton d'annulation respecté. Les pistes PostgreSQL et SQL Server sont couvertes hors de cette
/// solution, dans le harness de spike (<c>spikes/P4.ProviderComparison</c>), qui seul dispose des providers
/// serveur.
/// </para>
/// </summary>
public sealed class LowStockCreationProviderPortabilityTests : IDisposable
{
    private readonly string _workDirectory;

    public LowStockCreationProviderPortabilityTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p41-lotd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private OpticDbContext CreateContext(string databasePath, ParameterRecordingInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False");

        if (interceptor != null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new OpticDbContext(builder.Options);
    }

    /// <summary>Base jetable + un produit réel : l'alerte porte sur un <c>ProductId</c> existant.</summary>
    private async Task<(string DbPath, long ProductId)> SeedAsync(string fileName)
    {
        var dbPath = Path.Combine(_workDirectory, fileName);

        using (var schema = CreateContext(dbPath))
        {
            schema.Database.EnsureCreated();
        }

        using var context = CreateContext(dbPath);
        var supplier = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Fournisseur Lot D" });
        await new UnitOfWork(context).SaveChangesAsync();

        var product = await new ProductRepository(context).CreateAsync(new Product
        {
            Reference = "LOTD-REF-1",
            Name = "Produit Lot D",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            StockQuantity = 1,
            StockAlertThreshold = 5,
        });
        await new UnitOfWork(context).SaveChangesAsync();

        return (dbPath, product.ProductId);
    }

    private static Notification LowStockFor(long productId) => new()
    {
        Type = NotificationTypes.LowStock,
        Title = $"Stock bas : produit {productId}",
        Message = "Seuil d'alerte atteint",
        EntityId = productId,
        EntityType = NotificationEntityTypes.Product,
        IsRead = false,
        CreatedAt = new DateTime(2026, 7, 26, 9, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task PremiereCreation_OuvreLAlerteEtEcritUneSeuleLigne()
    {
        var (dbPath, productId) = await SeedAsync("lotd-first.db");

        using (var context = CreateContext(dbPath))
        {
            var created = await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId));
            created.Should().BeTrue("la première alerte active doit être ouverte par la base");
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId).Should().Be(1);
        verify.Notifications.AsNoTracking()
            .Count(n => n.EntityId == productId && n.ResolvedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task CreationEnDoublon_NAjouteAucuneLigne_EtRetourneFalse()
    {
        var (dbPath, productId) = await SeedAsync("lotd-duplicate.db");

        // Deux connexions DISTINCTES : le second poste ne « voit » rien de la session du premier, exactement comme
        // deux postes du réseau. La décision reste celle de la base.
        using var contextA = CreateContext(dbPath);
        using var contextB = CreateContext(dbPath);

        var first = await new NotificationRepository(contextA).TryCreateActiveLowStockAsync(LowStockFor(productId));
        var second = await new NotificationRepository(contextB).TryCreateActiveLowStockAsync(LowStockFor(productId));

        first.Should().BeTrue();
        second.Should().BeFalse("le doublon est refusé par la base, sans exception technique");

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId).Should().Be(1,
            "un refus ne doit laisser AUCUNE ligne supplémentaire, même résolue");
        verify.Notifications.AsNoTracking()
            .Count(n => n.EntityId == productId && n.ResolvedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task LaCreation_EstUneSeuleInstruction_SansLecturePrealable()
    {
        // Garantie centrale du Lot D : l'existence et l'écriture sont tranchées par la MÊME instruction. Un
        // « SELECT puis INSERT » applicatif rouvrirait la fenêtre de course que P3-8 avait fermée.
        var (dbPath, productId) = await SeedAsync("lotd-single-statement.db");

        var interceptor = new ParameterRecordingInterceptor();
        using (var context = CreateContext(dbPath, interceptor))
        {
            await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId));
        }

        interceptor.Commands.Should().HaveCount(1, "la primitive n'émet qu'UNE commande");

        var sql = interceptor.Commands[0].Sql;
        sql.TrimStart().Should().StartWith("INSERT");
        sql.Should().Contain("ON CONFLICT DO NOTHING", "dialecte SQLite/PostgreSQL de l'insertion conditionnelle");
        sql.Should().NotContain("SELECT", "aucune lecture préalable ne doit précéder l'écriture");
        sql.TrimEnd().TrimEnd(';').Should().NotContain(";", "une seule instruction, jamais un lot de deux");
    }

    [Fact]
    public async Task LesParametres_SontFabriquesParLeProviderCourant()
    {
        // Le défaut corrigé par le Lot D : des paramètres d'un type CONCRET étranger à la connexion. On vérifie
        // donc que chaque paramètre réellement transmis est du type que la connexion courante fabrique elle-même.
        var (dbPath, productId) = await SeedAsync("lotd-parameters.db");

        var interceptor = new ParameterRecordingInterceptor();
        using var context = CreateContext(dbPath, interceptor);

        Type expectedParameterType;
        using (var probe = context.Database.GetDbConnection().CreateCommand())
        {
            expectedParameterType = probe.CreateParameter().GetType();
        }

        await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId));

        var recorded = interceptor.Commands.Single();
        recorded.ParameterTypes.Should().HaveCount(7, "sept colonnes sont paramétrées");
        recorded.ParameterTypes.Should().OnlyContain(t => t == expectedParameterType,
            "aucun paramètre ne doit être d'un type concret étranger au provider monté");
    }

    [Fact]
    public async Task UnJetonDejaAnnule_InterrompLaCreation_SansRienEcrire()
    {
        // Le contrat asynchrone doit rester honoré : le jeton n'est pas ignoré en chemin.
        var (dbPath, productId) = await SeedAsync("lotd-cancellation.db");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        using (var context = CreateContext(dbPath))
        {
            var act = async () => await new NotificationRepository(context)
                .TryCreateActiveLowStockAsync(LowStockFor(productId), cancelled.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId).Should().Be(0);
    }

    [Fact]
    public async Task ApresResolution_UnNouvelEpisodeEstAccepte()
    {
        // L'index ne contraint que les alertes ACTIVES : la correction du Lot D ne doit pas transformer la
        // primitive en verrou historique.
        var (dbPath, productId) = await SeedAsync("lotd-new-episode.db");

        using (var context = CreateContext(dbPath))
        {
            (await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId)))
                .Should().BeTrue();
        }

        using (var resolve = CreateContext(dbPath))
        {
            await resolve.Notifications
                .Where(n => n.EntityId == productId && n.ResolvedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ResolvedAt,
                    new DateTime(2026, 7, 26, 10, 0, 0, DateTimeKind.Utc)));
        }

        using (var context = CreateContext(dbPath))
        {
            (await new NotificationRepository(context).TryCreateActiveLowStockAsync(LowStockFor(productId)))
                .Should().BeTrue("une nouvelle pénurie après réapprovisionnement ouvre légitimement une alerte");
        }

        using var verify = CreateContext(dbPath);
        verify.Notifications.AsNoTracking().Count(n => n.EntityId == productId).Should().Be(2);
        verify.Notifications.AsNoTracking()
            .Count(n => n.EntityId == productId && n.ResolvedAt == null).Should().Be(1);
    }

    /// <summary>Enregistre le texte ET le type concret des paramètres de chaque commande non-query exécutée.</summary>
    private sealed class ParameterRecordingInterceptor : DbCommandInterceptor
    {
        private readonly List<RecordedCommand> _commands = new();

        public IReadOnlyList<RecordedCommand> Commands => _commands;

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
            => _commands.Add(new RecordedCommand(
                command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(p => p.GetType()).ToList()));

        public sealed record RecordedCommand(string Sql, IReadOnlyList<Type> ParameterTypes);
    }
}
