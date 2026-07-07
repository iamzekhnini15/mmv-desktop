using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;
using MMV.Application.UseCases.Suppliers.ListSuppliers;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Suppliers;

/// <summary>
/// P2D-2 — Query use cases du module Fournisseurs (liste + fiche avec produits). Vérifie sur un <b>vrai SQLite
/// temporaire</b> la projection vers DTO (aucune entité EF), le tri des produits par nom, le cas introuvable, le cas
/// vide et les gardes.
/// </summary>
public sealed class SupplierQueryUseCasesTests : IDisposable
{
    private readonly string _workDirectory;

    public SupplierQueryUseCasesTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d-supplierq-" + Guid.NewGuid().ToString("N"));
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

    private static Product Product(long supplierId, string reference, string name) => new()
    {
        Reference = reference,
        Name = name,
        Category = ProductCategoryEnum.MONTURE,
        SupplierId = supplierId,
        PurchasePrice = 10m,
        SalePrice = 25m,
        StockQuantity = 3,
    };

    [Fact]
    public async Task ListSuppliers_ReturnsProjectedDtos()
    {
        var dbPath = PathFor("list.db");
        EnsureSchema(dbPath);

        using (var context = CreateContext(dbPath))
        {
            await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Alpha", ContactEmail = "a@x.io", Phone = "111", ReferenceCode = "A1" });
            await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Beta", Phone = "222", ReferenceCode = "B2" });
            await new UnitOfWork(context).SaveChangesAsync();
        }

        IReadOnlyList<SupplierListItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new ListSuppliersUseCase(new SupplierRepository(context));
            result = await useCase.ExecuteAsync(new ListSuppliersQuery());
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<SupplierListItemDto>();
        result.Should().ContainSingle(s => s.Name == "Alpha" && s.ContactEmail == "a@x.io" && s.ReferenceCode == "A1");
        result.Should().OnlyContain(s => s.Products.Count == 0, "la liste n'a jamais chargé la navigation Products (iso-fonctionnel)");
    }

    [Fact]
    public async Task ListSuppliers_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new ListSuppliersUseCase(new SupplierRepository(context)).ExecuteAsync(new ListSuppliersQuery());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSupplierWithProducts_ReturnsDetails_WithProductsSortedByName()
    {
        var dbPath = PathFor("details.db");
        EnsureSchema(dbPath);

        long supplierId;
        using (var context = CreateContext(dbPath))
        {
            var supplier = await new SupplierRepository(context).CreateAsync(new Supplier { Name = "Alpha", ReferenceCode = "A1" });
            await new UnitOfWork(context).SaveChangesAsync();
            supplierId = supplier.SupplierId;

            await new ProductRepository(context).CreateAsync(Product(supplierId, "R2", "Zebra"));
            await new ProductRepository(context).CreateAsync(Product(supplierId, "R1", "Apple"));
            await new UnitOfWork(context).SaveChangesAsync();
        }

        SupplierDetailsDto? result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new GetSupplierWithProductsUseCase(new SupplierRepository(context));
            result = await useCase.ExecuteAsync(new GetSupplierWithProductsQuery { SupplierId = supplierId });
        }

        result.Should().NotBeNull();
        result!.Name.Should().Be("Alpha");
        result.Products.Should().HaveCount(2);
        result.Products.Select(p => p.Name).Should().ContainInOrder("Apple", "Zebra");
        result.Products[0].SalePrice.Should().Be(25m);
    }

    [Fact]
    public async Task GetSupplierWithProducts_Missing_ReturnsNull()
    {
        var dbPath = PathFor("missing.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new GetSupplierWithProductsUseCase(new SupplierRepository(context))
            .ExecuteAsync(new GetSupplierWithProductsQuery { SupplierId = 404 });
        result.Should().BeNull();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new ListSuppliersUseCase(new SupplierRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => new GetSupplierWithProductsUseCase(new SupplierRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructors_RejectNullRepository()
    {
        ((Action)(() => _ = new ListSuppliersUseCase(null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new GetSupplierWithProductsUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
