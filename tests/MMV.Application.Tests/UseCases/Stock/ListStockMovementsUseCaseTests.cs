using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Stock.ListStockMovements;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Stock;

/// <summary>
/// P2D-4 — Query use case « Lister les mouvements de stock » (<see cref="ListStockMovementsUseCase"/>). Vérifie sur un
/// <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers DTO plats (jamais d'entité EF), la référence
/// produit imbriquée (Name / Reference), le tri décroissant par date hérité du repository, le cas vide et les gardes.
/// </summary>
public sealed class ListStockMovementsUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public ListStockMovementsUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d4-stockmov-" + Guid.NewGuid().ToString("N"));
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

    private static long SeedProduct(OpticDbContext context, string reference, string name)
    {
        var supplier = new Supplier { Name = "Fournisseur " + reference };
        context.Suppliers.Add(supplier);
        context.SaveChanges();

        var product = new Product
        {
            Reference = reference,
            Name = name,
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 10m,
            SalePrice = 20m,
            StockQuantity = 5,
        };
        context.Products.Add(product);
        context.SaveChanges();
        return product.ProductId;
    }

    [Fact]
    public async Task ListStockMovements_ReturnsProjectedDtos_SortedByDateDescending()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            var productId = SeedProduct(context, "R1", "Monture Alpha");

            context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.In,
                Quantity = 4,
                Reason = "Ancien",
                CreatedAt = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
            });
            context.StockMovements.Add(new StockMovement
            {
                ProductId = productId,
                MovementType = StockMovementType.Out,
                Quantity = 2,
                Reason = "Récent",
                CreatedAt = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
            });
            context.SaveChanges();
        }

        IReadOnlyList<StockMovementListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListStockMovementsUseCase(new StockMovementRepository(context));
            result = await useCase.ExecuteAsync(new ListStockMovementsQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<StockMovementListItemDto>();
        // Tri décroissant par date (hérité du repository) : le plus récent d'abord.
        result[0].Reason.Should().Be("Récent");
        result[1].Reason.Should().Be("Ancien");
        result[0].Product.Name.Should().Be("Monture Alpha");
        result[0].Product.Reference.Should().Be("R1");
        result[0].MovementType.Should().Be(StockMovementType.Out);
        result[0].Quantity.Should().Be(2);
    }

    [Fact]
    public async Task ListStockMovements_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListStockMovementsUseCase(new StockMovementRepository(context))
            .ExecuteAsync(new ListStockMovementsQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListStockMovementsUseCase(new StockMovementRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new ListStockMovementsUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
