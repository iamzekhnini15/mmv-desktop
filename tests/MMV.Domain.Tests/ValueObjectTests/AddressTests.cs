using FluentAssertions;
using MMV.Domain.ValueObjects;
using Xunit;

namespace MMV.Domain.Tests.ValueObjectTests;

public class AddressTests
{
    [Fact]
    public void Create_WithValidAddress_ShouldSucceed()
    {
        var address = new Address("123 Rue de Paris", "Paris", "75001", "FR", "Apt 42");
        address.Line1.Should().Be("123 Rue de Paris");
        address.City.Should().Be("Paris");
        address.PostalCode.Should().Be("75001");
        address.Country.Should().Be("FR");
        address.Line2.Should().Be("Apt 42");
    }

    [Fact]
    public void Create_WithNullLine1_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Address(null!, "75001", "Paris", "FR", null));
    }

    [Fact]
    public void Create_WithNullCity_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Address("123 Rue", "75001", null!, "FR", null));
    }

    [Fact]
    public void Create_WithNullCountry_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new Address("123 Rue", "75001", "Paris", null!, null));
    }

    [Fact]
    public void Create_WithoutLine2_ShouldSucceed()
    {
        var address = new Address("456 Avenue", "Lyon", "69001", "FR");
        address.Line1.Should().Be("456 Avenue");
        address.Line2.Should().BeNull();
        address.City.Should().Be("Lyon");
    }

    [Fact]
    public void Equality_ShouldCompareByAllFields()
    {
        var address1 = new Address("123 Rue", "Paris", "75001", "FR", "Apt 1");
        var address2 = new Address("123 Rue", "Paris", "75001", "FR", "Apt 1");
        address1.Should().Be(address2);
    }
}
