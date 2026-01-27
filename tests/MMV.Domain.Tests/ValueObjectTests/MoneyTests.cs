using FluentAssertions;
using MMV.Domain.ValueObjects;
using Xunit;

namespace MMV.Domain.Tests.ValueObjectTests;

public class MoneyTests
{
    [Fact]
    public void Create_WithValidAmount_ShouldRoundToTwoDecimals()
    {
        var money = new Money(19.995m, "EUR");
        money.Amount.Should().Be(20.00m);
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-10m, "EUR"));
    }

    [Fact]
    public void Zero_ShouldReturnZeroAmount()
    {
        var zero = Money.Zero();
        zero.Amount.Should().Be(0m);
        zero.Currency.Should().Be("EUR");
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        var money = new Money(99.99m, "EUR");
        var result = money.ToString();
        result.Should().Contain("99");
        result.Should().Contain("EUR");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.50)]
    [InlineData(9999.99)]
    public void Create_WithValidAmounts_ShouldSucceed(decimal amount)
    {
        var money = new Money(amount, "USD");
        money.Amount.Should().Be(amount);
        money.Currency.Should().Be("USD");
    }
}
