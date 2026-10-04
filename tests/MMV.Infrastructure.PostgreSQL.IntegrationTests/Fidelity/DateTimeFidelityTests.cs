using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data.Time;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Fidelity;

/// <summary>
/// N4 — fidélité temporelle contre PostgreSQL, AVEC assertions (ADR-PROD-DB-004 T7 ; le constat P4-1 n'était
/// qu'observationnel). Écritures et relectures par les repositories de production.
/// </summary>
public class DateTimeFidelityTests : PostgreSqlTestBase
{
    private static readonly DateTime Instant = new(2026, 10, 4, 10, 15, 30, DateTimeKind.Utc);

    [PostgreSqlFact]
    public async Task N4_utc_instant_round_trips_with_Kind_Utc_at_microsecond_precision()
    {
        var written = Instant.AddTicks(1_234_567); // ,1234567 s : 100 ns de plus que la résolution serveur
        await SaveSaleAsync("V-1", written);

        var read = (await ReadSaleAsync("V-1")).SaleDate;

        read.Kind.Should().Be(DateTimeKind.Utc);
        read.Should().Be(Instant.AddTicks(1_234_560), "timestamptz conserve la microseconde, pas les 100 ns");
    }

    [PostgreSqlFact]
    public async Task N4_server_stores_the_utc_instant_itself()
    {
        await SaveSaleAsync("V-1", Instant);

        var stored = await SchemaCatalog.QueryAsync(Database,
            """SELECT to_char("SaleDate" AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS') FROM "Sales" """);

        stored.Should().Equal("2026-10-04T10:15:30");
    }

    [PostgreSqlTheory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public async Task N4_non_utc_instant_is_refused_and_nothing_is_written(DateTimeKind kind)
    {
        var act = () => SaveSaleAsync("V-1", DateTime.SpecifyKind(Instant, kind));

        // Le convertisseur lève pendant SaveChanges, donc EF l'encapsule — même forme que sur SQLite.
        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerExceptionExactly<NonUtcDateTimeException>();
        (await SchemaCatalog.QueryAsync(Database, """SELECT count(*)::text FROM "Sales" """)).Should().Equal("0");
    }

    [PostgreSqlFact]
    public async Task N4_chronological_order_and_inclusive_range_are_preserved()
    {
        await SaveSaleAsync("V-late", Instant.AddHours(1));
        await SaveSaleAsync("V-first", Instant);
        await SaveSaleAsync("V-mid", Instant.AddTicks(10)); // une microseconde plus tard
        await SaveSaleAsync("V-out", Instant.AddHours(2));

        await using var context = NewContext();
        var sales = await new SaleRepository(context).GetByDateRangeAsync(Instant, Instant.AddHours(1));

        sales.Select(s => s.SaleNumber).Should().Equal("V-late", "V-mid", "V-first");
        sales.Should().OnlyContain(s => s.SaleDate.Kind == DateTimeKind.Utc);
    }

    [PostgreSqlFact]
    public async Task N4_civil_date_round_trips_as_a_date_without_time_zone_shift()
    {
        await using (var context = NewContext())
        {
            await using var unitOfWork = new UnitOfWork(context);
            await unitOfWork.Customers.CreateAsync(new Customer
            {
                FirstName = "Ada", LastName = "Lovelace", BirthDate = new DateOnly(1980, 2, 29)
            });
            await unitOfWork.SaveChangesAsync();
        }

        await using var read = NewContext();
        var customer = (await new CustomerRepository(read).GetAllAsync()).Single();

        customer.BirthDate.Should().Be(new DateOnly(1980, 2, 29));
        customer.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    private async Task SaveSaleAsync(string number, DateTime saleDate)
    {
        await using var context = NewContext();
        await using var unitOfWork = new UnitOfWork(context);
        await unitOfWork.Sales.CreateAsync(new Sale
        {
            SaleNumber = number,
            SaleDate = saleDate,
            TotalAmount = 10m,
            FinalAmount = 10m,
            PaymentMethod = PaymentMethod.Cash
        });
        await unitOfWork.SaveChangesAsync();
    }

    private async Task<Sale> ReadSaleAsync(string number)
    {
        await using var context = NewContext();
        return (await new SaleRepository(context).GetBySaleNumberAsync(number))!;
    }
}
