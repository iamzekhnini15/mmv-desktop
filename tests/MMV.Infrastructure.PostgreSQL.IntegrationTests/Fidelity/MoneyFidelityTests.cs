using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.ValueObjects;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.Repositories;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Fidelity;

/// <summary>
/// N3 — fidélité monétaire contre PostgreSQL (ADR-PROD-DB-003 M5, M6). Toutes les écritures et relectures
/// passent par <see cref="SaleRepository"/> et <see cref="UnitOfWork"/> de production.
/// </summary>
public class MoneyFidelityTests : PostgreSqlTestBase
{
    [PostgreSqlTheory]
    [InlineData("0.00")]
    [InlineData("0.01")]
    [InlineData("0.10")]
    [InlineData("1234567.89")]
    [InlineData("9999999999.99")]
    public async Task N3_amount_round_trips_exactly_with_two_decimals(string literal)
    {
        var amount = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);
        await SaveSaleAsync("V-1", finalAmount: amount);

        var read = await ReadSaleAsync("V-1");

        read.FinalAmount.Should().Be(amount);
        read.FinalAmount.Scale.Should().Be(2, "numeric(12,2) restitue toujours deux décimales");
    }

    [PostgreSqlFact]
    public async Task N3_amount_above_the_12_2_bound_is_rejected_never_truncated()
    {
        var act = () => SaveSaleAsync("V-1", finalAmount: 10_000_000_000.00m);

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.NumericValueOutOfRange);
    }

    [PostgreSqlTheory]
    [InlineData("2.345", "2.34")]
    [InlineData("2.355", "2.36")]
    [InlineData("0.125", "0.12")]
    public async Task N3_amount_rounded_by_Money_banker_rounding_persists_unchanged(string raw, string expected)
    {
        var money = new Money(decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture));
        money.Amount.Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

        await SaveSaleAsync("V-1", finalAmount: money.Amount);

        (await ReadSaleAsync("V-1")).FinalAmount.Should().Be(money.Amount);
    }

    /// <summary>
    /// Caractérisation, pas une exigence : PostgreSQL n'applique PAS l'arrondi bancaire à une valeur de plus
    /// de deux décimales, il arrondit « au plus loin de zéro ». L'arrondi métier reste donc l'affaire de
    /// <see cref="Money"/> (ADR-PROD-DB-003 §2.1, §5.1) ; une valeur non arrondie qui atteindrait la base
    /// divergerait de ToEven sur les cas médians.
    /// </summary>
    [PostgreSqlFact]
    public async Task N3_server_rounds_unrounded_midpoint_away_from_zero_not_to_even()
    {
        await SaveSaleAsync("V-1", finalAmount: 0.125m);

        var stored = (await ReadSaleAsync("V-1")).FinalAmount;

        stored.Should().Be(0.13m);
        stored.Should().NotBe(decimal.Round(0.125m, 2, MidpointRounding.ToEven));
    }

    [PostgreSqlFact]
    public async Task N3_M6_settlement_condition_RemainingAmount_greater_than_zero_is_exact()
    {
        var oneCent = await SaveSaleAsync("V-1", finalAmount: 100.10m, remaining: 0.01m);
        var zero = await SaveSaleAsync("V-2", finalAmount: 50.00m, remaining: 0.00m);
        var unknown = await SaveSaleAsync("V-3", finalAmount: 20.00m, remaining: null);

        await using var context = NewContext();
        var sales = new SaleRepository(context);

        (await sales.TrySettleRemainingBalanceAsync(oneCent)).Should().BeTrue();
        (await sales.TrySettleRemainingBalanceAsync(zero)).Should().BeFalse();
        (await sales.TrySettleRemainingBalanceAsync(unknown)).Should().BeFalse();

        var settled = await ReadSaleAsync("V-1");
        settled.DepositAmount.Should().Be(100.10m);
        settled.RemainingAmount.Should().Be(0.00m);
        settled.PaymentStatus.Should().Be(PaymentStatus.Paid);
    }

    [PostgreSqlFact]
    public async Task N3_M6_sum_of_final_amounts_is_exact()
    {
        var day = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        await SaveSaleAsync("V-1", finalAmount: 0.10m, saleDate: day);
        await SaveSaleAsync("V-2", finalAmount: 0.20m, saleDate: day);
        await SaveSaleAsync("V-3", finalAmount: 0.30m, saleDate: day);
        await SaveSaleAsync("V-4", finalAmount: 9_999_999.99m, saleDate: day);
        await SaveSaleAsync("V-out", finalAmount: 1_000m, saleDate: day.AddDays(10));

        await using var context = NewContext();
        var total = await new SaleRepository(context).GetTotalSalesAsync(day.AddHours(-1), day.AddHours(1));

        total.Should().Be(10_000_000.59m);
    }

    private async Task<long> SaveSaleAsync(
        string number, decimal finalAmount, decimal? remaining = null, DateTime? saleDate = null)
    {
        await using var context = NewContext();
        await using var unitOfWork = new UnitOfWork(context);
        var sale = new Sale
        {
            SaleNumber = number,
            SaleDate = saleDate ?? DateTime.UtcNow,
            TotalAmount = finalAmount,
            FinalAmount = finalAmount,
            RemainingAmount = remaining,
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = remaining > 0 ? PaymentStatus.Partial : PaymentStatus.Paid
        };
        await unitOfWork.Sales.CreateAsync(sale);
        await unitOfWork.SaveChangesAsync();
        return sale.SaleId;
    }

    private async Task<Sale> ReadSaleAsync(string number)
    {
        await using var context = NewContext();
        return (await new SaleRepository(context).GetBySaleNumberAsync(number))!;
    }
}
