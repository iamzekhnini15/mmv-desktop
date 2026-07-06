using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Repositories;
using Xunit;

namespace MMV.Application.Tests.UseCases.Sales;

/// <summary>
/// P2D-5 — Query use case « Historique d'achats d'un client » (<see cref="GetCustomerPurchaseHistoryUseCase"/>).
/// Vérifie sur un <b>vrai SQLite temporaire</b> (jamais InMemory) : la projection vers DTO plats (jamais d'entité EF),
/// le filtre par client, le tri décroissant par date hérité du repository, le cas vide et les gardes.
/// </summary>
public sealed class GetCustomerPurchaseHistoryUseCaseTests : IDisposable
{
    private readonly string _workDirectory;

    public GetCustomerPurchaseHistoryUseCaseTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2d5-saleshist-" + Guid.NewGuid().ToString("N"));
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

    private static long SeedCustomer(OpticDbContext context, string firstName)
    {
        var customer = new Customer { FirstName = firstName, LastName = "Test" };
        context.Customers.Add(customer);
        context.SaveChanges();
        return customer.CustomerId;
    }

    [Fact]
    public async Task GetPurchaseHistory_ReturnsProjectedDtos_SortedByDateDescending_FilteredByCustomer()
    {
        var dbPath = PathFor("hist.db");
        EnsureSchema(dbPath);

        long customerId;
        using (var context = CreateContext(dbPath))
        {
            customerId = SeedCustomer(context, "Alice");
            var otherId = SeedCustomer(context, "Bob");

            context.Sales.Add(new Sale
            {
                CustomerId = customerId,
                SaleNumber = "VTE-2026-0001",
                SaleDate = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc),
                FinalAmount = 100m,
                DepositAmount = 40m,
                RemainingAmount = 60m,
                EstimatedDelivery = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = SaleStatus.AwaitingLenses,
            });
            context.Sales.Add(new Sale
            {
                CustomerId = customerId,
                SaleNumber = "VTE-2026-0002",
                SaleDate = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc),
                FinalAmount = 250m,
                Status = SaleStatus.Delivered,
            });
            // Vente d'un AUTRE client : ne doit pas apparaître.
            context.Sales.Add(new Sale
            {
                CustomerId = otherId,
                SaleNumber = "VTE-2026-0003",
                SaleDate = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc),
                FinalAmount = 999m,
                Status = SaleStatus.Draft,
            });
            context.SaveChanges();
        }

        IReadOnlyList<CustomerSaleItemDto> result;
        using (var context = CreateContext(dbPath))
        {
            var useCase = new GetCustomerPurchaseHistoryUseCase(new SaleRepository(context));
            result = await useCase.ExecuteAsync(new GetCustomerPurchaseHistoryQuery { CustomerId = customerId });
        }

        result.Should().HaveCount(2);
        result.Should().AllBeOfType<CustomerSaleItemDto>();
        // Tri décroissant par date (hérité du repository) : le plus récent d'abord.
        result[0].SaleNumber.Should().Be("VTE-2026-0002");
        result[1].SaleNumber.Should().Be("VTE-2026-0001");
        result[0].FinalAmount.Should().Be(250m);
        result[0].Status.Should().Be(SaleStatus.Delivered);
        result[1].DepositAmount.Should().Be(40m);
        result[1].RemainingAmount.Should().Be(60m);
        result[1].EstimatedDelivery.Should().Be(new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetPurchaseHistory_NoData_ReturnsEmpty()
    {
        var dbPath = PathFor("empty.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        var result = await new GetCustomerPurchaseHistoryUseCase(new SaleRepository(context))
            .ExecuteAsync(new GetCustomerPurchaseHistoryQuery { CustomerId = 12345 });
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        var dbPath = PathFor("null.db");
        EnsureSchema(dbPath);
        using var context = CreateContext(dbPath);
        await ((Func<Task>)(() => new GetCustomerPurchaseHistoryUseCase(new SaleRepository(context)).ExecuteAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_RejectsNullRepository()
    {
        ((Action)(() => _ = new GetCustomerPurchaseHistoryUseCase(null!))).Should().Throw<ArgumentNullException>();
    }
}
