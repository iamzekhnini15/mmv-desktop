using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.CreateProduct;
using MMV.Application.UseCases.Products.DeleteProduct;
using MMV.Application.UseCases.Products.SetProductActive;
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
/// P3-4B — Règles métier et intégrité du domaine Produits, vérifiées sur un <b>vrai SQLite temporaire</b> :
/// unicité normalisée de la référence (+ filet DB), validation métier réellement appliquée, cohérence
/// catégorie/détails, garde-fou de suppression (FK Restrict), activation/désactivation et sélecteurs.
/// </summary>
public sealed class ProductBusinessRulesTests : IDisposable
{
    private readonly string _workDirectory;

    public ProductBusinessRulesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p34b-product-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    private static void EnsureSchema(string databasePath)
    {
        using var context = CreateContext(databasePath);
        context.Database.EnsureCreated();
    }

    private static async Task<long> SeedSupplierAsync(string dbPath)
    {
        using var context = CreateContext(dbPath);
        var created = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Fournisseur" });
        await new UnitOfWork(context).SaveChangesAsync();
        return created.SupplierId;
    }

    private static CreateProductUseCase CreateUseCase(OpticDbContext ctx) =>
        new(new ProductRepository(ctx), new SupplierRepository(ctx), new UnitOfWork(ctx), new EfTransactionRunner(ctx));

    private static UpdateProductUseCase UpdateUseCase(OpticDbContext ctx) =>
        new(new ProductRepository(ctx), new SupplierRepository(ctx), new UnitOfWork(ctx), new EfTransactionRunner(ctx));

    private static DeleteProductUseCase DeleteUseCase(OpticDbContext ctx) =>
        new(new ProductRepository(ctx), new UnitOfWork(ctx));

    private static CreateProductCommand ValidCreate(long supplierId, string reference, ProductCategoryEnum category = ProductCategoryEnum.MONTURE) => new()
    {
        Reference = reference, Name = "Produit", Category = category,
        PurchasePrice = 10m, SalePrice = 25m, StockQuantity = 3, StockAlertThreshold = 5, SupplierId = supplierId,
    };

    private static async Task<long> CreateProductAsync(string dbPath, long supplierId, string reference, ProductCategoryEnum category = ProductCategoryEnum.MONTURE)
    {
        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(ValidCreate(supplierId, reference, category));
        result.IsValid.Should().BeTrue();
        return result.ProductId;
    }

    // ---------------------------------------------------------------------------------------------- Référence

    [Fact]
    public async Task Create_EmptyReference_ReturnsValidationError()
    {
        var dbPath = PathFor("ref-empty.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(ValidCreate(supplierId, "   "));

        result.IsValid.Should().BeFalse();
        result.ProductId.Should().Be(0);
        ctx.Products.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_TrimsExternalWhitespace_AndNormalizes()
    {
        var dbPath = PathFor("ref-trim.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
            (await CreateUseCase(ctx).ExecuteAsync(ValidCreate(supplierId, "  REF-1  "))).IsValid.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.Reference.Should().Be("REF-1");
        product.NormalizedReference.Should().Be("REF-1");
    }

    [Theory]
    [InlineData("ABC-123")]   // doublon exact
    [InlineData("abc-123")]   // casse différente
    [InlineData("  ABC-123 ")] // espaces externes
    public async Task Create_DuplicateReference_ReturnsValidationError_AndDoesNotPersistSecond(string secondReference)
    {
        var dbPath = PathFor("ref-dup.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        await CreateProductAsync(dbPath, supplierId, "ABC-123");

        CreateProductResult result;
        using (var ctx = CreateContext(dbPath))
            result = await CreateUseCase(ctx).ExecuteAsync(ValidCreate(supplierId, secondReference));

        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().HaveCount(1);
    }

    [Fact]
    public async Task Create_AccentedReference_NormalizesLikeToUpperInvariant_AndDetectsDuplicate()
    {
        // Contexte francophone : la référence accentuée « réf-é » doit se normaliser comme ToUpperInvariant
        // (« RÉF-É »), et « RÉF-É » saisie ensuite doit être détectée comme doublon par la garde applicative
        // (parité exacte entre l'écriture et l'index unique — cf. backfill migration aligné).
        var dbPath = PathFor("ref-accent.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
            (await CreateUseCase(ctx).ExecuteAsync(ValidCreate(supplierId, "réf-é"))).IsValid.Should().BeTrue();

        using (var verify = CreateContext(dbPath))
        {
            var product = verify.Products.AsNoTracking().Single();
            product.NormalizedReference.Should().Be(Product.NormalizeReference("réf-é"));
            product.NormalizedReference.Should().Be("RÉF-É");
        }

        // Une seconde référence équivalente après normalisation (« RÉF-É ») est refusée par la garde applicative.
        using (var ctx = CreateContext(dbPath))
        {
            var second = await CreateUseCase(ctx).ExecuteAsync(ValidCreate(supplierId, "RÉF-É"));
            second.IsValid.Should().BeFalse();
            second.ValidationErrors.Should().ContainSingle()
                .Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);
        }

        using (var verify = CreateContext(dbPath))
            verify.Products.AsNoTracking().Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_ToOtherProductsReference_ReturnsValidationError()
    {
        var dbPath = PathFor("ref-upd-dup.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        await CreateProductAsync(dbPath, supplierId, "AAA");
        var secondId = await CreateProductAsync(dbPath, supplierId, "BBB");

        UpdateProductResult result;
        using (var ctx = CreateContext(dbPath))
            result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateProductCommand
            {
                ProductId = secondId, Reference = "aaa", Name = "X", Category = ProductCategoryEnum.MONTURE,
                PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId,
            });

        result.ProductFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Single(p => p.ProductId == secondId).Reference.Should().Be("BBB");
    }

    [Fact]
    public async Task Update_KeepingOwnReference_Succeeds()
    {
        var dbPath = PathFor("ref-upd-self.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "SELF");

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateProductCommand
            {
                ProductId = id, Reference = "self", Name = "New name", Category = ProductCategoryEnum.MONTURE,
                PurchasePrice = 5m, SalePrice = 9m, SupplierId = supplierId,
            });
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.Name.Should().Be("New name");
        product.NormalizedReference.Should().Be("SELF");
    }

    [Fact]
    public async Task Db_UniqueNormalizedReference_RejectsSecondEquivalentInsert()
    {
        var dbPath = PathFor("ref-db.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        ctx.Products.Add(new Product { Reference = "ABC-123", Name = "A", Category = ProductCategoryEnum.MONTURE, SupplierId = supplierId, SalePrice = 1m });
        ctx.Products.Add(new Product { Reference = "abc-123", Name = "B", Category = ProductCategoryEnum.MONTURE, SupplierId = supplierId, SalePrice = 1m });

        var act = async () => await ctx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ConcurrentEquivalentWrite_ThatBypassesAppGuard_SurfacesNeutralPersistenceError()
    {
        // Simule un second poste dont la vérification applicative a raté la course : l'écriture perdante est
        // rejetée par l'index unique de la base et TRADUITE en PersistenceException neutre (jamais un message
        // SQLite/EF brut), via le mécanisme existant PersistenceErrorMapper.
        var dbPath = PathFor("ref-concurrent.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        await CreateProductAsync(dbPath, supplierId, "ABC-123");

        using var ctx = CreateContext(dbPath);
        var runner = new EfTransactionRunner(ctx);
        var act = async () => await runner.RunAsync(async token =>
        {
            ctx.Products.Add(new Product { Reference = "abc-123", Name = "Racing", Category = ProductCategoryEnum.MONTURE, SupplierId = supplierId, SalePrice = 1m });
            await ctx.SaveChangesAsync(token);
        });

        var ex = (await act.Should().ThrowAsync<PersistenceException>()).Which;
        ex.Category.Should().Be(PersistenceErrorCategory.UniqueConstraint);
        ex.Message.Should().NotContainEquivalentOf("SQLITE");
        ex.Message.Should().NotContainEquivalentOf("constraint failed");

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().HaveCount(1);
    }

    [Fact]
    public async Task Create_ConcurrentCollision_ReturnsSameStableDuplicateResult_NoException()
    {
        // Contrat PUBLIC du use case : quand la garde applicative rate la course (simulée via un repository dont
        // ExistsByNormalizedReferenceAsync renvoie toujours false) mais que l'index unique rejette l'écriture
        // perdante, le use case renvoie EXACTEMENT le même résultat métier stable que la garde pré-écriture —
        // aucune PersistenceException ne s'échappe, aucun message provider brut n'est exposé.
        var dbPath = PathFor("concurrent-create.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        await CreateProductAsync(dbPath, supplierId, "ABC-123");

        using var ctx = CreateContext(dbPath);
        var raceLosing = new RaceLosingProductRepository(ctx);
        var useCase = new CreateProductUseCase(raceLosing, new SupplierRepository(ctx), new UnitOfWork(ctx), new EfTransactionRunner(ctx));

        CreateProductResult result = await useCase.ExecuteAsync(ValidCreate(supplierId, "abc-123"));

        result.IsValid.Should().BeFalse();
        result.ProductId.Should().Be(0);
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);
        result.ValidationErrors[0].Message.Should().NotContainEquivalentOf("SQLITE");

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_ConcurrentCollision_ReturnsSameStableDuplicateResult_NoException()
    {
        var dbPath = PathFor("concurrent-update.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        await CreateProductAsync(dbPath, supplierId, "AAA");
        var secondId = await CreateProductAsync(dbPath, supplierId, "BBB");

        using var ctx = CreateContext(dbPath);
        var raceLosing = new RaceLosingProductRepository(ctx);
        var useCase = new UpdateProductUseCase(raceLosing, new SupplierRepository(ctx), new UnitOfWork(ctx), new EfTransactionRunner(ctx));

        UpdateProductResult result = await useCase.ExecuteAsync(new UpdateProductCommand
        {
            ProductId = secondId, Reference = "aaa", Name = "X", Category = ProductCategoryEnum.MONTURE,
            PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId,
        });

        result.ProductFound.Should().BeTrue();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().ContainSingle()
            .Which.Message.Should().Be(CreateProductUseCase.DuplicateReferenceMessage);

        using var verify = CreateContext(dbPath);
        // La référence d'affichage de B est intacte : l'écriture perdante a été annulée (rollback du runner).
        verify.Products.AsNoTracking().Single(p => p.ProductId == secondId).Reference.Should().Be("BBB");
    }

    /// <summary>
    /// Repository réel dont la seule garde d'unicité applicative est neutralisée : simule honnêtement un poste
    /// qui a raté la course (la garde ne « voit » pas encore le doublon concurrent). Réimplémente
    /// <see cref="IProductRepository"/> pour ne remplacer QUE
    /// <see cref="IProductRepository.ExistsByNormalizedReferenceAsync"/> ; tout le reste (insertion, chargement)
    /// délègue au vrai <see cref="ProductRepository"/>, si bien que l'index unique de la base agit réellement.
    /// </summary>
    private sealed class RaceLosingProductRepository : ProductRepository, IProductRepository
    {
        public RaceLosingProductRepository(OpticDbContext context) : base(context) { }

        Task<bool> IProductRepository.ExistsByNormalizedReferenceAsync(string normalizedReference, long excludingProductId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    // ---------------------------------------------------------------------------------------------- Validation

    [Theory]
    [InlineData(-1, 25, null, 3, "PurchasePrice")]
    [InlineData(10, -1, null, 3, "SalePrice")]
    [InlineData(10, 25, -1.0, 3, "RecommendedPrice")]
    [InlineData(50, 25, null, 3, "SalePrice")]  // vente < achat
    [InlineData(10, 25, null, -1, "StockQuantity")]
    public async Task Create_InvalidValues_ReturnsValidationError_AndDoesNotPersist(decimal purchase, decimal sale, double? recommended, int stock, string _)
    {
        var dbPath = PathFor($"val-{purchase}-{sale}-{recommended}-{stock}.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(new CreateProductCommand
        {
            Reference = "V", Name = "Produit", Category = ProductCategoryEnum.MONTURE,
            PurchasePrice = purchase, SalePrice = sale, RecommendedPrice = (decimal?)recommended,
            StockQuantity = stock, SupplierId = supplierId,
        });

        result.IsValid.Should().BeFalse();
        ctx.Products.AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ValidValues_Succeeds()
    {
        var dbPath = PathFor("val-ok.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using var ctx = CreateContext(dbPath);
        var result = await CreateUseCase(ctx).ExecuteAsync(new CreateProductCommand
        {
            Reference = "OK", Name = "Produit", Category = ProductCategoryEnum.MONTURE,
            PurchasePrice = 10m, SalePrice = 25m, RecommendedPrice = 30m, StockQuantity = 3, SupplierId = supplierId,
        });

        result.IsValid.Should().BeTrue();
        result.ProductId.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------------------------ Catégorie / détails

    [Fact]
    public async Task Create_Verre_PersistsOnlyGlassDetail()
    {
        var dbPath = PathFor("cat-verre.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
            await CreateUseCase(ctx).ExecuteAsync(new CreateProductCommand
            {
                Reference = "G", Name = "Verre", Category = ProductCategoryEnum.VERRE,
                PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId, GlassType = "SF",
            });

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking()
            .Include(p => p.GlassDetail).Include(p => p.LensDetail).Include(p => p.AccessoryDetail).Single();
        product.GlassDetail.Should().NotBeNull();
        product.LensDetail.Should().BeNull();
        product.AccessoryDetail.Should().BeNull();
    }

    [Fact]
    public async Task Create_Lentille_PersistsOnlyLensDetail()
    {
        var dbPath = PathFor("cat-lentille.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
            await CreateUseCase(ctx).ExecuteAsync(new CreateProductCommand
            {
                Reference = "L", Name = "Lentille", Category = ProductCategoryEnum.LENTILLE,
                PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId, LensBrand = "Acuvue",
            });

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking()
            .Include(p => p.GlassDetail).Include(p => p.LensDetail).Include(p => p.AccessoryDetail).Single();
        product.LensDetail.Should().NotBeNull();
        product.GlassDetail.Should().BeNull();
        product.AccessoryDetail.Should().BeNull();
    }

    [Fact]
    public async Task Create_Accessory_PersistsOnlyAccessoryDetail()
    {
        var dbPath = PathFor("cat-acc.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        using (var ctx = CreateContext(dbPath))
            await CreateUseCase(ctx).ExecuteAsync(new CreateProductCommand
            {
                Reference = "A", Name = "Monture", Category = ProductCategoryEnum.MONTURE,
                PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId, AccessoryColor = "Noir",
            });

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking()
            .Include(p => p.GlassDetail).Include(p => p.LensDetail).Include(p => p.AccessoryDetail).Single();
        product.AccessoryDetail.Should().NotBeNull();
        product.GlassDetail.Should().BeNull();
        product.LensDetail.Should().BeNull();
    }

    [Theory]
    [InlineData(ProductCategoryEnum.VERRE, "GlassType", "SF", ProductCategoryEnum.MONTURE)]     // VERRE → MONTURE supprime GlassDetail
    [InlineData(ProductCategoryEnum.MONTURE, "AccessoryColor", "Noir", ProductCategoryEnum.LENTILLE)] // MONTURE → LENTILLE supprime AccessoryDetail
    [InlineData(ProductCategoryEnum.LENTILLE, "LensBrand", "Acuvue", ProductCategoryEnum.VERRE)] // LENTILLE → VERRE supprime LensDetail
    public async Task Update_CategoryChange_RemovesIncompatibleDetail(ProductCategoryEnum from, string field, string value, ProductCategoryEnum to)
    {
        var dbPath = PathFor($"cat-change-{from}-{to}.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);

        long id;
        using (var ctx = CreateContext(dbPath))
        {
            var create = new CreateProductCommand { Reference = "C", Name = "P", Category = from, PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId };
            create = field switch
            {
                "GlassType" => WithGlassType(create, value),
                "AccessoryColor" => WithAccessoryColor(create, value),
                "LensBrand" => WithLensBrand(create, value),
                _ => create,
            };
            id = (await CreateUseCase(ctx).ExecuteAsync(create)).ProductId;
        }

        using (var ctx = CreateContext(dbPath))
            await UpdateUseCase(ctx).ExecuteAsync(new UpdateProductCommand
            {
                ProductId = id, Reference = "C", Name = "P", Category = to, PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId,
            });

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking()
            .Include(p => p.GlassDetail).Include(p => p.LensDetail).Include(p => p.AccessoryDetail).Single();

        // Après changement de catégorie, aucun détail incompatible ne subsiste (au plus le détail compatible, ici vide).
        var detailCount = (product.GlassDetail is null ? 0 : 1) + (product.LensDetail is null ? 0 : 1) + (product.AccessoryDetail is null ? 0 : 1);
        detailCount.Should().Be(0);
    }

    private static CreateProductCommand WithGlassType(CreateProductCommand c, string v) => new()
    { Reference = c.Reference, Name = c.Name, Category = c.Category, PurchasePrice = c.PurchasePrice, SalePrice = c.SalePrice, SupplierId = c.SupplierId, GlassType = v };
    private static CreateProductCommand WithAccessoryColor(CreateProductCommand c, string v) => new()
    { Reference = c.Reference, Name = c.Name, Category = c.Category, PurchasePrice = c.PurchasePrice, SalePrice = c.SalePrice, SupplierId = c.SupplierId, AccessoryColor = v };
    private static CreateProductCommand WithLensBrand(CreateProductCommand c, string v) => new()
    { Reference = c.Reference, Name = c.Name, Category = c.Category, PurchasePrice = c.PurchasePrice, SalePrice = c.SalePrice, SupplierId = c.SupplierId, LensBrand = v };

    // ----------------------------------------------------------------------------------------- Suppression

    [Fact]
    public async Task Delete_UnusedProduct_RemovesItAndDetailsCascade()
    {
        var dbPath = PathFor("del-unused.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        long id;
        using (var ctx = CreateContext(dbPath))
            id = (await CreateUseCase(ctx).ExecuteAsync(new CreateProductCommand
            { Reference = "DEL", Name = "P", Category = ProductCategoryEnum.MONTURE, PurchasePrice = 1m, SalePrice = 2m, SupplierId = supplierId, AccessoryColor = "Noir" })).ProductId;

        using (var ctx = CreateContext(dbPath))
            (await DeleteUseCase(ctx).ExecuteAsync(new DeleteProductCommand { ProductId = id })).ProductFound.Should().BeTrue();

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().BeEmpty();
        verify.Set<AccessoryDetail>().AsNoTracking().Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_UsedBySaleItem_Throws_AndSaleItemIntact()
    {
        var dbPath = PathFor("del-sale.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "PS");
        using (var ctx = CreateContext(dbPath))
        {
            var sale = new Sale { SaleNumber = "S-1", TotalAmount = 10m, FinalAmount = 10m };
            sale.SaleItems.Add(new SaleItem { ProductId = id, Quantity = 1, UnitPrice = 10m, TotalPrice = 10m });
            ctx.Sales.Add(sale);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbPath))
        {
            var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteProductCommand { ProductId = id });
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Be(DeleteProductUseCase.ProductInUseMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.Products.AsNoTracking().Should().ContainSingle(p => p.ProductId == id);
        verify.SaleItems.AsNoTracking().Single().ProductId.Should().Be(id);
    }

    [Fact]
    public async Task Delete_UsedByOrderItem_Throws_AndOrderItemIntact()
    {
        var dbPath = PathFor("del-order.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "PO");
        using (var ctx = CreateContext(dbPath))
        {
            var sale = new Sale { SaleNumber = "S-2", TotalAmount = 10m, FinalAmount = 10m };
            ctx.Sales.Add(sale);
            await ctx.SaveChangesAsync();
            var order = new Order { OrderNumber = "O-1", SaleId = sale.SaleId };
            order.OrderItems.Add(new OrderItem { ProductId = id, Quantity = 1, UnitPrice = 10m });
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbPath))
        {
            var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteProductCommand { ProductId = id });
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Be(DeleteProductUseCase.ProductInUseMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.OrderItems.AsNoTracking().Single().ProductId.Should().Be(id);
    }

    [Fact]
    public async Task Delete_UsedByStockMovement_Throws_AndMovementIntact()
    {
        var dbPath = PathFor("del-stock.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "PM");
        using (var ctx = CreateContext(dbPath))
        {
            ctx.StockMovements.Add(new StockMovement { ProductId = id, MovementType = StockMovementType.In, Quantity = 5 });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = CreateContext(dbPath))
        {
            var act = async () => await DeleteUseCase(ctx).ExecuteAsync(new DeleteProductCommand { ProductId = id });
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Be(DeleteProductUseCase.ProductInUseMessage);
        }

        using var verify = CreateContext(dbPath);
        verify.StockMovements.AsNoTracking().Single().ProductId.Should().Be(id);
    }

    [Fact]
    public async Task DirectDbDelete_OfReferencedProduct_RejectedByRestrict()
    {
        var dbPath = PathFor("del-direct.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "PD");
        using (var ctx = CreateContext(dbPath))
        {
            ctx.StockMovements.Add(new StockMovement { ProductId = id, MovementType = StockMovementType.In, Quantity = 5 });
            await ctx.SaveChangesAsync();
        }

        using var deleteCtx = CreateContext(dbPath);
        var product = await deleteCtx.Products.SingleAsync(p => p.ProductId == id);
        deleteCtx.Products.Remove(product);
        var act = async () => await deleteCtx.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ------------------------------------------------------------------------------------------ Activation

    [Fact]
    public async Task SetActive_Deactivate_Reactivate_AndIdempotent()
    {
        var dbPath = PathFor("active.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "ACT");

        using (var ctx = CreateContext(dbPath))
        {
            var useCase = new SetProductActiveUseCase(new ProductRepository(ctx), new UnitOfWork(ctx));
            (await useCase.ExecuteAsync(new SetProductActiveCommand { ProductId = id, IsActive = false })).IsActive.Should().BeFalse();
            // Idempotent : re-désactiver renvoie un succès sans erreur.
            var again = await useCase.ExecuteAsync(new SetProductActiveCommand { ProductId = id, IsActive = false });
            again.ProductFound.Should().BeTrue();
            again.IsActive.Should().BeFalse();
        }
        using (var verify = CreateContext(dbPath))
            verify.Products.AsNoTracking().Single().IsActive.Should().BeFalse();

        using (var ctx = CreateContext(dbPath))
            (await new SetProductActiveUseCase(new ProductRepository(ctx), new UnitOfWork(ctx))
                .ExecuteAsync(new SetProductActiveCommand { ProductId = id, IsActive = true })).IsActive.Should().BeTrue();
        using (var verify = CreateContext(dbPath))
            verify.Products.AsNoTracking().Single().IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task SetActive_MissingProduct_ReturnsNotFound()
    {
        var dbPath = PathFor("active-missing.db"); EnsureSchema(dbPath);
        using var ctx = CreateContext(dbPath);
        var result = await new SetProductActiveUseCase(new ProductRepository(ctx), new UnitOfWork(ctx))
            .ExecuteAsync(new SetProductActiveCommand { ProductId = 999, IsActive = false });
        result.ProductFound.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------- P3-5 Stock découplé

    [Fact]
    public async Task Update_CatalogEdit_DoesNotRewriteStock_ButUpdatesOtherFields()
    {
        var dbPath = PathFor("p35-catalog.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "STK"); // stock initial 3 (ValidCreate)

        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateProductCommand
            {
                ProductId = id, Reference = "STK", Name = "Nouveau nom", Category = ProductCategoryEnum.MONTURE,
                PurchasePrice = 10m, SalePrice = 40m, StockQuantity = 999, // valeur volontairement absurde : doit être ignorée
                SupplierId = supplierId,
            });
            result.ProductFound.Should().BeTrue();
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single(p => p.ProductId == id);
        product.StockQuantity.Should().Be(3, "P3-5 : l'édition catalogue ne réécrit jamais le stock (command.StockQuantity=999 ignoré)");
        product.Name.Should().Be("Nouveau nom", "les autres champs sont bien mis à jour");
        product.SalePrice.Should().Be(40m);
    }

    [Fact]
    public async Task Update_DoesNotOverwriteConcurrentDecrement()
    {
        var dbPath = PathFor("p35-lostupdate.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "RACE"); // stock initial 3

        // Un autre poste décrémente le stock (3 → 1) APRÈS l'ouverture du formulaire d'édition catalogue.
        using (var ctx = CreateContext(dbPath))
        {
            await new EfStockMutationService(ctx).DecrementStockAsync(id, 2);
        }

        // Le formulaire (chargé quand le stock valait 3) enregistre une édition catalogue portant encore StockQuantity = 3.
        using (var ctx = CreateContext(dbPath))
        {
            var result = await UpdateUseCase(ctx).ExecuteAsync(new UpdateProductCommand
            {
                ProductId = id, Reference = "RACE", Name = "Édité", Category = ProductCategoryEnum.MONTURE,
                PurchasePrice = 10m, SalePrice = 30m, StockQuantity = 3, // valeur périmée du formulaire
                SupplierId = supplierId,
            });
            result.IsValid.Should().BeTrue();
        }

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single(p => p.ProductId == id);
        product.StockQuantity.Should().Be(1, "le décrément concurrent (→ 1) n'est PAS écrasé par l'édition catalogue (plus de lost update)");
        product.Name.Should().Be("Édité", "l'édition catalogue est bien appliquée par ailleurs");
    }

    [Fact]
    public async Task SetActive_OnlyChangesIsActive()
    {
        var dbPath = PathFor("active-scope.db"); EnsureSchema(dbPath);
        var supplierId = await SeedSupplierAsync(dbPath);
        var id = await CreateProductAsync(dbPath, supplierId, "SCOPE");

        using (var ctx = CreateContext(dbPath))
            await new SetProductActiveUseCase(new ProductRepository(ctx), new UnitOfWork(ctx))
                .ExecuteAsync(new SetProductActiveCommand { ProductId = id, IsActive = false });

        using var verify = CreateContext(dbPath);
        var product = verify.Products.AsNoTracking().Single();
        product.Reference.Should().Be("SCOPE");
        product.PurchasePrice.Should().Be(10m);
        product.SalePrice.Should().Be(25m);
        product.StockQuantity.Should().Be(3);
        product.IsActive.Should().BeFalse();
    }
}
