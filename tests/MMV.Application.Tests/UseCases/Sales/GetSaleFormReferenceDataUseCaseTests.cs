using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Sales.GetSaleFormReferenceData;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P2D-7A — Query use case « Charger les données de référence du formulaire de vente »
/// (<see cref="GetSaleFormReferenceDataUseCase"/>). Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) :
/// la sélection de l'ordonnance <b>la plus récente</b> du client (projetée en DTO plat), la projection du catalogue
/// produit (avec détails verre pour la compatibilité), le cas vide et les gardes (query nulle, repositories nuls).
/// </summary>
public sealed class GetSaleFormReferenceDataUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public GetSaleFormReferenceDataUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d7-saleref-" + Guid.NewGuid().ToString("N"));
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

    private static GetSaleFormReferenceDataUseCase BuildUseCase(OpticDbContext context) =>
        new(new ProductRepository(context), new PrescriptionRepository(context));

    [Fact]
    public async Task Execute_ReturnsLatestPrescription_AndProjectedCatalog()
    {
        var dbPath = PathFor("ref.db");
        EnsureSchema(dbPath);

        long customerId;
        using (var context = CreateContext(dbPath))
        {
            var customer = new Customer { FirstName = "Jean", LastName = "Dupont" };
            context.Customers.Add(customer);
            context.SaveChanges();
            customerId = customer.CustomerId;

            // Deux ordonnances : la plus récente (2024-06) doit être retenue.
            context.Prescriptions.Add(new Prescription { CustomerId = customerId, IssueDate = new DateOnly(2024, 1, 1), DoctorName = "Dr Ancien", OdSphere = -1.0 });
            context.Prescriptions.Add(new Prescription { CustomerId = customerId, IssueDate = new DateOnly(2024, 6, 1), DoctorName = "Dr Recent", OdSphere = -2.5 });

            context.Products.Add(new Product
            {
                Reference = "V-1", Name = "Verre X", Category = ProductCategoryEnum.VERRE,
                Supplier = new Supplier { Name = "F" }, SalePrice = 100m, PurchasePrice = 40m, StockQuantity = 9, IsActive = true,
                GlassDetail = new GlassDetail { GlassType = GlassType.SF, PowerLimitMin = -6m, PowerLimitMax = 6m },
            });
            context.SaveChanges();
        }

        SaleFormReferenceDataDto result;
        using (var context = CreateContext(dbPath))
        {
            result = await BuildUseCase(context).ExecuteAsync(new GetSaleFormReferenceDataQuery { CustomerId = customerId });
        }

        result.ActivePrescription.Should().NotBeNull();
        result.ActivePrescription!.DoctorName.Should().Be("Dr Recent", "l'ordonnance la plus récente doit être retenue");
        result.ActivePrescription.CustomerId.Should().Be(customerId);
        result.ActivePrescription.OdSphere.Should().Be(-2.5);

        result.Products.Should().ContainSingle();
        var product = result.Products[0];
        product.Should().BeOfType<SaleProductPickerItemDto>();
        product.Reference.Should().Be("V-1");
        product.SalePrice.Should().Be(100m);
        product.StockQuantity.Should().Be(9);
        product.GlassDetail.Should().NotBeNull();
        product.GlassDetail!.GlassType.Should().Be(GlassType.SF);
        product.GlassDetail.PowerLimitMin.Should().Be(-6m);
        product.GlassDetail.PowerLimitMax.Should().Be(6m);
    }

    [Fact]
    public async Task Execute_NoPrescription_ReturnsNullActivePrescription()
    {
        var dbPath = PathFor("no-presc.db");
        EnsureSchema(dbPath);

        long customerId;
        using (var context = CreateContext(dbPath))
        {
            var customer = new Customer { FirstName = "Sans", LastName = "Ordo" };
            context.Customers.Add(customer);
            context.SaveChanges();
            customerId = customer.CustomerId;
        }

        using var ctx = CreateContext(dbPath);
        var result = await BuildUseCase(ctx).ExecuteAsync(new GetSaleFormReferenceDataQuery { CustomerId = customerId });

        result.ActivePrescription.Should().BeNull();
        result.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_DoesNotReturnDomainEntities()
    {
        var dbPath = PathFor("no-entity.db");
        EnsureSchema(dbPath);

        long customerId;
        using (var context = CreateContext(dbPath))
        {
            var customer = new Customer { FirstName = "A", LastName = "B" };
            context.Customers.Add(customer);
            context.SaveChanges();
            customerId = customer.CustomerId;
            context.Prescriptions.Add(new Prescription { CustomerId = customerId, IssueDate = new DateOnly(2024, 3, 1) });
            context.Products.Add(new Product
            {
                Reference = "R", Name = "N", Category = ProductCategoryEnum.MONTURE,
                Supplier = new Supplier { Name = "S" }, SalePrice = 1m, PurchasePrice = 1m, StockQuantity = 1,
            });
            context.SaveChanges();
        }

        using var ctx = CreateContext(dbPath);
        var result = await BuildUseCase(ctx).ExecuteAsync(new GetSaleFormReferenceDataQuery { CustomerId = customerId });

        ((object)result.ActivePrescription!).Should().NotBeAssignableTo<Prescription>();
        result.Products.Should().NotContain(dto => (object)dto is Product);
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => BuildUseCase(context).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepositories()
    {
        using var context = CreateContext(PathFor("ctor.db"));
        ((Action)(() => _ = new GetSaleFormReferenceDataUseCase(null!, new PrescriptionRepository(context))))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new GetSaleFormReferenceDataUseCase(new ProductRepository(context), null!)))
            .Should().Throw<ArgumentNullException>();
    }
}
