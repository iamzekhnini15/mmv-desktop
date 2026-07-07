using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Products.ListProducts;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Products;

/// <summary>
/// P2D-7D — Query use case « Lister les produits » (<see cref="ListProductsUseCase"/>). Vérifie sur un
/// <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers des <see cref="ProductListItemDto"/> plats
/// (jamais l'entité EF <c>Product</c>), le portage du fournisseur et des détails par catégorie (Verre / Lentille /
/// Accessoire), le tri décroissant de l'historique de commandes, le cas vide et les gardes.
/// </summary>
public sealed class ListProductsUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListProductsUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d7-products-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task ListProducts_ProjectsScalars_Supplier_And_CategoryDetails()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var supplier = new Supplier { Name = "Fourni A" };

            context.Products.Add(new Product
            {
                Reference = "V-1", Name = "Verre X", Category = ProductCategoryEnum.VERRE,
                Supplier = supplier, PurchasePrice = 40m, SalePrice = 100m, StockQuantity = 7,
                StockAlertThreshold = 3, IsActive = true, EntryDate = new DateTime(2024, 1, 1),
                GlassDetail = new GlassDetail
                {
                    Material = GlassMaterial.ORGANIQUE, GlassType = GlassType.SF, Diameter = "65",
                    Index = 1.5m, PowerLimitMin = -6m, PowerLimitMax = 6m,
                },
            });
            context.Products.Add(new Product
            {
                Reference = "L-1", Name = "Lentille Y", Category = ProductCategoryEnum.LENTILLE,
                Supplier = supplier, PurchasePrice = 5m, SalePrice = 15m, StockQuantity = 20,
                LensDetail = new LensDetail
                {
                    Brand = "Acuvue", Model = "Oasys", Material = LensMaterial.SOUPLE,
                    LensType = LensType.UNIFOCAL, Diameter = 14.2m, BaseCurve = 8.4m,
                    IsColored = false, Duration = LensDuration.MENSUELLE,
                },
            });
            context.Products.Add(new Product
            {
                Reference = "M-1", Name = "Monture Z", Category = ProductCategoryEnum.MONTURE,
                Supplier = supplier, PurchasePrice = 30m, SalePrice = 90m, StockQuantity = 4,
                AccessoryDetail = new AccessoryDetail { Color = "Noir", Size = "52", Material = "Acétate" },
            });
            context.SaveChanges();
        }

        IReadOnlyList<ProductListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            result = await new ListProductsUseCase(new ProductRepository(context)).ExecuteAsync(new ListProductsQuery());
        }

        result.Should().HaveCount(3);
        result.Should().AllBeOfType<ProductListItemDto>();

        var verre = result.Single(p => p.Reference == "V-1");
        verre.Name.Should().Be("Verre X");
        verre.Category.Should().Be(ProductCategoryEnum.VERRE);
        verre.SalePrice.Should().Be(100m);
        verre.StockQuantity.Should().Be(7);
        verre.StockAlertThreshold.Should().Be(3);
        verre.IsActive.Should().BeTrue();
        verre.Supplier.Should().NotBeNull();
        verre.Supplier!.Name.Should().Be("Fourni A");
        verre.GlassDetail.Should().NotBeNull();
        verre.GlassDetail!.GlassType.Should().Be(GlassType.SF);
        verre.GlassDetail.PowerLimitMin.Should().Be(-6m);
        verre.GlassDetail.PowerLimitMax.Should().Be(6m);
        verre.LensDetail.Should().BeNull();
        verre.AccessoryDetail.Should().BeNull();

        var lentille = result.Single(p => p.Reference == "L-1");
        lentille.LensDetail.Should().NotBeNull();
        lentille.LensDetail!.Brand.Should().Be("Acuvue");
        lentille.LensDetail.Material.Should().Be(LensMaterial.SOUPLE);
        lentille.LensDetail.LensType.Should().Be(LensType.UNIFOCAL);
        lentille.LensDetail.Duration.Should().Be(LensDuration.MENSUELLE);
        lentille.GlassDetail.Should().BeNull();

        var monture = result.Single(p => p.Reference == "M-1");
        monture.AccessoryDetail.Should().NotBeNull();
        monture.AccessoryDetail!.Color.Should().Be("Noir");
        monture.AccessoryDetail.Size.Should().Be("52");
        monture.AccessoryDetail.Material.Should().Be("Acétate");
    }

    [Fact]
    public async Task ListProducts_OrderHistory_IsSortedByOrderDateDescending()
    {
        var dbPath = PathFor("history.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var supplier = new Supplier { Name = "Fourni B" };
            var product = new Product
            {
                Reference = "P-1", Name = "Produit", Category = ProductCategoryEnum.MONTURE,
                Supplier = supplier, PurchasePrice = 10m, SalePrice = 20m, StockQuantity = 5,
            };
            context.Products.Add(product);
            context.SaveChanges();

            var sale = new Sale { SaleNumber = "S-1", FinalAmount = 20m };
            var older = new Order { OrderNumber = "CMD-OLD", OrderDate = new DateTime(2024, 1, 1), Status = OrderStatus.Delivered, Sale = sale };
            older.OrderItems.Add(new OrderItem { Product = product, Quantity = 1, UnitPrice = 20m, ItemType = OrderItemType.Frame });
            var newer = new Order { OrderNumber = "CMD-NEW", OrderDate = new DateTime(2024, 6, 1), Status = OrderStatus.Ready, Sale = sale };
            newer.OrderItems.Add(new OrderItem { Product = product, Quantity = 2, UnitPrice = 18m, ItemType = OrderItemType.Frame });
            context.Orders.AddRange(older, newer);
            context.SaveChanges();
        }

        IReadOnlyList<ProductListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            result = await new ListProductsUseCase(new ProductRepository(context)).ExecuteAsync(new ListProductsQuery());
        }

        var projected = result.Single(p => p.Reference == "P-1");
        projected.OrderHistory.Should().HaveCount(2);
        projected.OrderHistory[0].Order.Should().NotBeNull();
        projected.OrderHistory[0].Order!.OrderNumber.Should().Be("CMD-NEW", "l'historique est trié du plus récent au plus ancien");
        projected.OrderHistory[0].Quantity.Should().Be(2);
        projected.OrderHistory[1].Order!.OrderNumber.Should().Be("CMD-OLD");
    }

    [Fact]
    public async Task ListProducts_DoesNotReturnDomainEntities()
    {
        var dbPath = PathFor("no-entity.db");
        EnsureSchema(dbPath);
        using (var context = CreateContext(dbPath))
        {
            context.Products.Add(new Product
            {
                Reference = "X-1", Name = "X", Category = ProductCategoryEnum.MONTURE,
                Supplier = new Supplier { Name = "S" }, SalePrice = 1m, PurchasePrice = 1m, StockQuantity = 1,
            });
            context.SaveChanges();
        }

        using var ctx = CreateContext(dbPath);
        var result = await new ListProductsUseCase(new ProductRepository(ctx)).ExecuteAsync(new ListProductsQuery());

        result.Should().ContainSingle().Which.Should().BeOfType<ProductListItemDto>();
        result.Should().NotContain(dto => (object)dto is Product);
    }

    [Fact]
    public async Task ListProducts_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListProductsUseCase(new ProductRepository(context)).ExecuteAsync(new ListProductsQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListProductsUseCase(new ProductRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListProductsUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
