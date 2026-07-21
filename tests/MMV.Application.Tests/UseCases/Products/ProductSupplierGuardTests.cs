using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.UpdateProduct;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Products;

/// <summary>
/// P3-9 — Fournisseur <b>obligatoire et existant</b> lors des écritures produit. Corrige le défaut 🔴-3 :
/// <c>command.SupplierId ?? 0</c> écrivait un identifiant qu'aucun fournisseur ne porte, transformant une donnée
/// <b>manquante</b> en violation d'intégrité (<c>DbUpdateException</c>) au lieu d'une erreur de saisie lisible.
/// C'était le symétrique exact, côté produit, du défaut de suppression fournisseur.
/// </summary>
public sealed class ProductSupplierGuardTests : IDisposable
{
    private readonly string _workDirectory;

    public ProductSupplierGuardTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p39-product-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* nettoyage best-effort */ }
    }

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private string NewDatabase(string fileName)
    {
        var dbPath = Path.Combine(_workDirectory, fileName);
        using var context = CreateContext(dbPath);
        context.Database.EnsureCreated();
        return dbPath;
    }

    private static CreateProductUseCase CreateUseCase(OpticDbContext ctx, ISupplierRepository? suppliers = null)
        => new(new ProductRepository(ctx), suppliers ?? new SupplierRepository(ctx), new UnitOfWork(ctx), new EfTransactionRunner(ctx));

    private static UpdateProductUseCase UpdateUseCase(OpticDbContext ctx, ISupplierRepository? suppliers = null)
        => new(new ProductRepository(ctx), suppliers ?? new SupplierRepository(ctx), new UnitOfWork(ctx), new EfTransactionRunner(ctx));

    private static async Task<long> SeedSupplierAsync(string dbPath)
    {
        using var ctx = CreateContext(dbPath);
        var created = await new SupplierRepository(ctx).CreateAsync(new Supplier { Name = "Fournisseur" });
        await new UnitOfWork(ctx).SaveChangesAsync();
        return created.SupplierId;
    }

    private static CreateProductCommand CreateCommand(long? supplierId, string reference = "REF-1") => new()
    {
        Reference = reference,
        Name = "Produit",
        Category = ProductCategoryEnum.MONTURE,
        PurchasePrice = 5m,
        SalePrice = 10m,
        SupplierId = supplierId,
    };

    private static UpdateProductCommand UpdateCommand(long productId, long? supplierId, string reference = "REF-1") => new()
    {
        ProductId = productId,
        Reference = reference,
        Name = "Produit modifié",
        Category = ProductCategoryEnum.MONTURE,
        PurchasePrice = 5m,
        SalePrice = 12m,
        SupplierId = supplierId,
    };

    private static async Task<long> SeedProductAsync(string dbPath, long supplierId, bool isActive = true)
    {
        using var ctx = CreateContext(dbPath);
        var product = new Product
        {
            Reference = "REF-1", Name = "Produit", Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplierId, SalePrice = 10m, IsActive = isActive,
        };
        ctx.Products.Add(product);
        await ctx.SaveChangesAsync();
        return product.ProductId;
    }

    // ==================================================================
    // CREATE PRODUCT
    // ==================================================================

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Create_SansFournisseurValide_EstRefuse_AvantTouteEcriture(long? supplierId)
    {
        var dbPath = NewDatabase($"create-req-{supplierId ?? -99}.db");

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(CreateCommand(supplierId));

        result.IsValid.Should().BeFalse();
        result.ProductId.Should().Be(0);
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.SupplierRequiredMessage);

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().BeEmpty("aucun produit n'est persisté lors du refus");
    }

    [Fact]
    public async Task Create_NEcritJamaisUnSupplierIdEgalAZero()
    {
        // Garde de non-régression directe contre la réintroduction du « ?? 0 ».
        var dbPath = NewDatabase("create-zero.db");

        using (var ctx = CreateContext(dbPath))
        {
            await CreateUseCase(ctx).ExecuteAsync(CreateCommand(null));
            await CreateUseCase(ctx).ExecuteAsync(CreateCommand(0));
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().NotContain(p => p.SupplierId == 0);
        verify.Products.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_FournisseurInexistant_EstRefuse_SansPersistenceException()
    {
        var dbPath = NewDatabase("create-inconnu.db");

        using var ctx = CreateContext(dbPath);
        var act = async () => await CreateUseCase(ctx).ExecuteAsync(CreateCommand(4242));

        var result = (await act.Should().NotThrowAsync("une erreur de saisie n'est pas une panne technique")).Which;

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.SupplierNotFoundMessage);
        result.ValidationErrors[0].Message.Should().NotContainEquivalentOf("SQLITE");

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_FournisseurExistant_EstAccepte()
    {
        var dbPath = NewDatabase("create-ok.db");
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
        {
            var result = await CreateUseCase(ctx).ExecuteAsync(CreateCommand(supplierId));
            result.IsValid.Should().BeTrue();
            result.ProductId.Should().BeGreaterThan(0);
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single().SupplierId.Should().Be(supplierId);
    }

    // ==================================================================
    // UPDATE PRODUCT
    // ==================================================================

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Update_SansFournisseurValide_EstRefuse_ProduitInchange(long? supplierId)
    {
        var dbPath = NewDatabase($"update-req-{supplierId ?? -99}.db");
        var existingSupplier = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, existingSupplier);

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(UpdateCommand(productId, supplierId));

            result.ProductFound.Should().BeTrue();
            result.IsValid.Should().BeFalse();
            result.ValidationErrors.Should().ContainSingle()
                .Which.Message.Should().Be(CreateProductUseCase.SupplierRequiredMessage);
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.SupplierId.Should().Be(existingSupplier, "le produit reste inchangé lors du refus");
        product.Name.Should().Be("Produit");
    }

    [Fact]
    public async Task Update_FournisseurInexistant_EstRefuse_ProduitInchange()
    {
        var dbPath = NewDatabase("update-inconnu.db");
        var existingSupplier = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, existingSupplier);

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(UpdateCommand(productId, 4242));

            result.ProductFound.Should().BeTrue();
            result.IsValid.Should().BeFalse();
            result.ValidationErrors.Should().ContainSingle()
                .Which.Message.Should().Be(CreateProductUseCase.SupplierNotFoundMessage);
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.SupplierId.Should().Be(existingSupplier);
        product.Name.Should().Be("Produit");
    }

    [Fact]
    public async Task Update_FournisseurExistant_EstAccepte()
    {
        var dbPath = NewDatabase("update-ok.db");
        var first = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, first);

        long second;
        using (var ctx = CreateContext(dbPath))
        {
            var created = await new SupplierRepository(ctx).CreateAsync(new Supplier { Name = "Autre" });
            await new UnitOfWork(ctx).SaveChangesAsync();
            second = created.SupplierId;
        }

        using (var ctx = CreateContext(dbPath))
        {
            (await UpdateUseCase(ctx).ExecuteAsync(UpdateCommand(productId, second))).IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single().SupplierId.Should().Be(second);
    }

    [Fact]
    public async Task Update_MemeFournisseurQueLActuel_EstQuandMemeVerifie()
    {
        // L'existence est revérifiée même quand l'identifiant fourni est celui du fournisseur ACTUEL : rien ne
        // garantit qu'il existe encore au moment de la sauvegarde.
        //
        // Note honnête sur ce qu'il est possible de construire : l'état « un produit référence un fournisseur
        // disparu » est INFABRICABLE, y compris hors use case — la FK RESTRICT le refuse (prouvé par
        // Course_SuppressionPuisCreationDeProduit_EstRefuseeSansOrphelin). C'est précisément la garantie du
        // domaine : aucun orphelin n'existe. L'invariant vérifiable ici est donc que la garde est bel et bien
        // CONSULTÉE — et non court-circuitée au motif que l'identifiant n'a pas changé.
        var dbPath = NewDatabase("update-identique.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, supplierId);

        using (var ctx = CreateContext(dbPath))
        {
            var counting = new CountingSupplierRepository(ctx);
            var result = await UpdateUseCase(ctx, counting).ExecuteAsync(UpdateCommand(productId, supplierId));

            result.IsValid.Should().BeTrue();
            counting.ExistsCallCount.Should().Be(1, "l'existence est vérifiée même pour un fournisseur inchangé");
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single().SupplierId.Should().Be(supplierId);
    }

    [Fact]
    public async Task Update_ProduitInactif_ResteModifiable()
    {
        // P3-9 n'introduit aucune restriction nouvelle sur l'édition d'un produit inactif.
        var dbPath = NewDatabase("update-inactif.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, supplierId, isActive: false);

        using (var ctx = CreateContext(dbPath))
        {
            (await UpdateUseCase(ctx).ExecuteAsync(UpdateCommand(productId, supplierId))).IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.Name.Should().Be("Produit modifié");
        product.IsActive.Should().BeFalse("l'édition catalogue ne réactive pas le produit");
    }

    [Fact]
    public async Task Update_ProduitIntrouvable_PrimeSurLaGardeFournisseur()
    {
        var dbPath = NewDatabase("update-produit-absent.db");

        using var ctx = CreateContext(dbPath);
        var result = await UpdateUseCase(ctx).ExecuteAsync(UpdateCommand(999, null));

        result.ProductFound.Should().BeFalse();
        result.IsValid.Should().BeTrue("l'absence du produit est signalée par son propre axe");
    }

    // ==================================================================
    // COURSE DÉTERMINISTE — le filet FK
    // ==================================================================

    [Fact]
    public async Task Create_VerificationPerimee_LaFkArbitre_EtLErreurEstTraduite()
    {
        // Une vérification d'existence ne VERROUILLE pas le fournisseur jusqu'à l'écriture. On reproduit la course
        // sans aucun délai : le port d'existence renvoie un résultat POSITIF PÉRIMÉ au moment de la garde, alors
        // que le fournisseur n'existe plus réellement. La FK RESTRICT refuse l'écriture, et le use case traduit
        // l'échec en message métier stable — jamais une PersistenceException, jamais un texte SQLite.
        var dbPath = NewDatabase("course-create.db");

        using var ctx = CreateContext(dbPath);
        var stale = new StaleThenTruthfulSupplierRepository(ctx);
        var act = async () => await CreateUseCase(ctx, stale).ExecuteAsync(CreateCommand(4242));

        var result = (await act.Should().NotThrowAsync()).Which;

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.SupplierNotFoundMessage);
        result.ValidationErrors[0].Message.Should().NotContainEquivalentOf("SQLITE");
        result.ValidationErrors[0].Message.Should().NotContainEquivalentOf("constraint");

        stale.ExistsCallCount.Should().Be(2, "une garde périmée, puis une confirmation de diagnostic");

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().BeEmpty("aucune écriture partielle n'est conservée");
    }

    [Fact]
    public async Task Update_VerificationPerimee_LaFkArbitre_EtLErreurEstTraduite()
    {
        var dbPath = NewDatabase("course-update.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        var productId = await SeedProductAsync(dbPath, supplierId);

        using var ctx = CreateContext(dbPath);
        var stale = new StaleThenTruthfulSupplierRepository(ctx);
        var act = async () => await UpdateUseCase(ctx, stale).ExecuteAsync(UpdateCommand(productId, 4242));

        var result = (await act.Should().NotThrowAsync()).Which;

        result.ProductFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.SupplierNotFoundMessage);

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single().SupplierId.Should()
            .Be(supplierId, "le rollback du runner a annulé l'écriture perdante");
    }

    [Fact]
    public async Task UneViolationDeContrainteSansRapport_NEstPasEtiqueteeFournisseurIntrouvable()
    {
        // Honnêteté du diagnostic : PersistenceErrorMapper classe TOUTE contrainte SQLite en ConstraintViolation.
        // Le use case ne traduit donc que si le fournisseur a RÉELLEMENT disparu. Ici il existe bel et bien, et
        // c'est l'unicité de la référence qui rejette l'écriture : le message doit rester celui du doublon, pas
        // « fournisseur introuvable ».
        var dbPath = NewDatabase("contrainte-autre.db");
        var supplierId = await SeedSupplierAsync(dbPath);
        await SeedProductAsync(dbPath, supplierId);

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(CreateCommand(supplierId, "REF-1"));

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);
    }

    [Fact]
    public void LesConstructeurs_ExigentLePortFournisseur()
    {
        ((Action)(() => _ = new CreateProductUseCase(null!, null!, null!, null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new UpdateProductUseCase(null!, null!, null!, null!))).Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Repository réel dont le <b>premier</b> test d'existence renvoie un <c>true</c> périmé — exactement ce que
    /// verrait une garde exécutée juste avant qu'un autre poste supprime le fournisseur. Les appels suivants
    /// disent la vérité : c'est la lecture de diagnostic, postérieure à l'échec, qui reflète l'état réel.
    /// Modélise la chronologie de la course sans aucun délai ni thread.
    /// </summary>
    /// <summary>
    /// Repository réel qui se contente de <b>compter</b> les tests d'existence, sans en altérer le résultat :
    /// permet de prouver que la garde est réellement consultée, y compris quand le fournisseur ne change pas.
    /// </summary>
    private sealed class CountingSupplierRepository : SupplierRepository, ISupplierRepository
    {
        public CountingSupplierRepository(OpticDbContext context) : base(context) { }

        public int ExistsCallCount { get; private set; }

        Task<bool> ISupplierRepository.ExistsFreshAsync(long supplierId, CancellationToken cancellationToken)
        {
            ExistsCallCount++;
            return base.ExistsFreshAsync(supplierId, cancellationToken);
        }
    }

    private sealed class StaleThenTruthfulSupplierRepository : SupplierRepository, ISupplierRepository
    {
        public StaleThenTruthfulSupplierRepository(OpticDbContext context) : base(context) { }

        public int ExistsCallCount { get; private set; }

        Task<bool> ISupplierRepository.ExistsFreshAsync(long supplierId, CancellationToken cancellationToken)
        {
            ExistsCallCount++;
            return ExistsCallCount == 1
                ? Task.FromResult(true)
                : base.ExistsFreshAsync(supplierId, cancellationToken);
        }
    }
}
