using System.Data.Common;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MMV.Application.UseCases.Suppliers.DeleteSupplier;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Suppliers;

/// <summary>
/// P3-9 — Revue ciblée avant commit : preuve directe (1) du SQL réellement produit par
/// <see cref="ISupplierRepository.TryDeleteIfUnusedAsync"/> et (2) de l'état du change tracker après
/// <c>ExecuteDeleteAsync</c>, sur <b>vraie base SQLite jetable</b>. Même mécanisme d'interception que
/// <c>LowStockConcurrencyAndQueryCountTests</c> (P3-8) — pas de nouvel outillage introduit.
/// </summary>
public sealed class SupplierDeletionSqlAndTrackerTests : IDisposable
{
    private readonly string _workDirectory;

    public SupplierDeletionSqlAndTrackerTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p39-sql-tracker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* nettoyage best-effort */ }
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

    private string NewDatabase(string fileName)
    {
        var dbPath = PathFor(fileName);
        using var context = CreateContext(dbPath);
        context.Database.EnsureCreated();
        return dbPath;
    }

    private static async Task<long> SeedSupplierAsync(string dbPath, string name = "Essilor")
    {
        using var ctx = CreateContext(dbPath);
        var created = await new SupplierRepository(ctx).CreateAsync(new Supplier { Name = name });
        await new UnitOfWork(ctx).SaveChangesAsync();
        return created.SupplierId;
    }

    private static async Task SeedProductAsync(string dbPath, long supplierId, string reference)
    {
        using var ctx = CreateContext(dbPath);
        ctx.Products.Add(new Product
        {
            Reference = reference,
            Name = "Produit " + reference,
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplierId,
            SalePrice = 10m,
        });
        await ctx.SaveChangesAsync();
    }

    // ==================================================================
    // §3 — SQL réellement produit
    // ==================================================================

    [Fact]
    public async Task TryDeleteIfUnused_SansProduit_ProduitUneSeuleCommandeDelete_AvecConditionNotExists()
    {
        var dbPath = NewDatabase("sql-libre.db");
        var id = await SeedSupplierAsync(dbPath);

        var interceptor = new CommandRecordingInterceptor();
        using var ctx = CreateContext(dbPath, interceptor);

        (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(id)).Should().BeTrue();

        // Une seule commande exécutée pour toute l'opération : condition et écriture indissociables.
        interceptor.Commands.Should().ContainSingle();

        var sql = interceptor.Commands[0];
        sql.Should().ContainEquivalentOf("DELETE", "il s'agit d'une suppression, pas d'une lecture préalable");
        sql.Should().Contain("Suppliers");
        sql.Should().ContainEquivalentOf("NOT", "la condition d'absence de produit doit être NIÉE dans le WHERE/EXISTS");
        sql.Should().ContainEquivalentOf("EXISTS");
        sql.Should().Contain("Products", "la condition porte sur la table Products, pas sur une navigation chargée");

        // Requête paramétrée : l'identifiant est lié via un paramètre nommé, jamais concaténé en littéral dans le
        // texte de commande (le texte de commande EF référence "@__supplierId_0", pas une valeur littérale).
        sql.Should().Contain("@__supplierId_0", "l'identifiant doit être un paramètre nommé, jamais concaténé en clair");
        sql.Should().NotMatchRegex(@"SupplierId""\s*=\s*\d", "aucune valeur littérale ne doit apparaître à la place du paramètre");
    }

    [Fact]
    public async Task TryDeleteIfUnused_AvecProduit_ProduitUneSeuleCommandeDelete_QuiNAffecteAucuneLigne()
    {
        var dbPath = NewDatabase("sql-lie.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        await SeedProductAsync(dbPath, supplierId, "P-SQL");

        var interceptor = new CommandRecordingInterceptor();
        using var ctx = CreateContext(dbPath, interceptor);

        (await new SupplierRepository(ctx).TryDeleteIfUnusedAsync(supplierId)).Should().BeFalse();

        // Même chemin, même unique commande — le refus n'ajoute AUCUNE lecture préalable ni second aller-retour.
        interceptor.Commands.Should().ContainSingle();
        interceptor.Commands[0].Should().ContainEquivalentOf("DELETE");

        using var verify = CreateContext(dbPath);
        verify.Suppliers.AsNoTracking().Should().ContainSingle();
        verify.Products.AsNoTracking().Should().ContainSingle();
    }

    // ==================================================================
    // §4 — Change tracker après ExecuteDeleteAsync (même DbContext)
    // ==================================================================

    [Fact]
    public async Task TryDeleteIfUnused_ApresSucces_AucuneInstancePerimeeNEstResuscitee_ParFindAsync()
    {
        // ExecuteDeleteAsync contourne le change tracker : une entité Supplier chargée AVANT dans la MÊME portée
        // reste, en l'absence de traitement, marquée "suivie" (Unchanged) alors que la ligne a été physiquement
        // supprimée. FindAsync consulte le cache local AVANT d'interroger la base : sans détachement ciblé après
        // succès, un second appel FindAsync sur le même contexte renverrait l'instance PÉRIMÉE sans requêter SQLite.
        var dbPath = NewDatabase("tracker-succes.db");
        var id = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        var repository = new SupplierRepository(ctx);

        var loaded = await repository.GetByIdAsync(id);
        loaded.Should().NotBeNull();
        ctx.Entry(loaded!).State.Should().NotBe(EntityState.Detached, "l'entité doit être suivie avant la suppression");

        (await repository.TryDeleteIfUnusedAsync(id)).Should().BeTrue();

        var afterDelete = await ctx.Suppliers.FindAsync(id);
        afterDelete.Should().BeNull(
            "après une suppression réussie, FindAsync sur le MÊME contexte ne doit plus ressusciter l'instance suivie périmée");

        using var freshContext = CreateContext(dbPath);
        (await freshContext.Suppliers.FindAsync(id)).Should().BeNull("un nouveau contexte confirme l'absence physique");
    }

    [Fact]
    public async Task TryDeleteIfUnused_ApresEchec_AucuneInstanceNEstDetachee()
    {
        // Contre-preuve : quand la suppression est REFUSÉE (produit lié), l'entité suivie doit rester suivie et
        // inchangée — aucun détachement ne doit se produire sur un chemin qui n'a rien supprimé.
        var dbPath = NewDatabase("tracker-echec.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        await SeedProductAsync(dbPath, supplierId, "P-TRK");

        using var ctx = CreateContext(dbPath);
        var repository = new SupplierRepository(ctx);

        var loaded = await repository.GetByIdAsync(supplierId);
        loaded.Should().NotBeNull();

        (await repository.TryDeleteIfUnusedAsync(supplierId)).Should().BeFalse();

        ctx.Entry(loaded!).State.Should().NotBe(EntityState.Detached,
            "un refus de suppression ne doit détacher aucune entité suivie");

        var stillTracked = await ctx.Suppliers.FindAsync(supplierId);
        stillTracked.Should().BeSameAs(loaded, "l'instance suivie reste valide : rien n'a été supprimé");
    }

    [Fact]
    public async Task TryDeleteIfUnused_ApresSucces_NeDetacheAucunAutreFournisseurSuivi()
    {
        // Le détachement, s'il existe, doit être CIBLÉ sur l'identifiant supprimé — jamais un ChangeTracker.Clear()
        // global qui affecterait des entités sans rapport (produits suivis dans la même portée, par exemple).
        var dbPath = NewDatabase("tracker-isole.db");
        var cible = await SeedSupplierAsync(dbPath, "Cible");
        var voisin = await SeedSupplierAsync(dbPath, "Voisin");

        using var ctx = CreateContext(dbPath);
        var repository = new SupplierRepository(ctx);

        var loadedCible = await repository.GetByIdAsync(cible);
        var loadedVoisin = await repository.GetByIdAsync(voisin);
        loadedCible.Should().NotBeNull();
        loadedVoisin.Should().NotBeNull();

        (await repository.TryDeleteIfUnusedAsync(cible)).Should().BeTrue();

        ctx.Entry(loadedVoisin!).State.Should().NotBe(EntityState.Detached,
            "seule l'entité supprimée doit être détachée, jamais un voisin sans rapport");
        (await ctx.Suppliers.FindAsync(voisin)).Should().BeSameAs(loadedVoisin);
    }

    // ==================================================================
    // §5 — Diagnostic après suppression refusée, avec instance périmée dans le tracker
    // ==================================================================

    [Fact]
    public async Task DeleteSupplierUseCase_AvecInstanceSuivieDejaSupprimeeAilleurs_ProduitSupplierFoundFalse_SansFauxMessage()
    {
        // Une instance Supplier chargée dans CETTE portée avant que le fournisseur ne soit supprimé par un AUTRE
        // contexte : le use case ne doit jamais s'appuyer sur cette instance suivie ni sur l'ExistsAsync périmé
        // pour diagnostiquer — TryDeleteIfUnusedAsync réévalue l'état réel, et ExistsFreshAsync confirme.
        var dbPath = NewDatabase("usecase-perime.db");
        var id = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        var repository = new SupplierRepository(ctx);

        // Charge et suit l'entité dans CETTE portée.
        var loaded = await repository.GetByIdAsync(id);
        loaded.Should().NotBeNull();

        // Supprimé physiquement par un AUTRE poste, via un AUTRE contexte — celui-ci l'ignore.
        using (var other = CreateContext(dbPath))
        {
            (await new SupplierRepository(other).TryDeleteIfUnusedAsync(id)).Should().BeTrue();
        }

        // L'ExistsAsync hérité (FindAsync) verrait encore l'instance suivie, PÉRIMÉE.
        (await repository.ExistsAsync(id)).Should().BeTrue("l'instance suivie dans cette portée est désormais périmée");

        var useCase = new DeleteSupplierUseCase(repository, new EfTransactionRunner(ctx));
        var result = await useCase.ExecuteAsync(new DeleteSupplierCommand { SupplierId = id });

        // TryDeleteIfUnusedAsync réévalue l'état réel (0 ligne affectée, déjà supprimée) ; le diagnostic
        // (ExistsFreshAsync) confirme l'absence réelle : SupplierFound = false, jamais un refus métier fabriqué
        // sur la foi d'une instance suivie périmée.
        result.SupplierFound.Should().BeFalse();
    }

    /// <summary>Intercepteur EF enregistrant le texte de chaque commande réellement exécutée (calque P3-8).</summary>
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
