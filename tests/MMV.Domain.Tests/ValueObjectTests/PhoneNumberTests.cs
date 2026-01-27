using FluentAssertions;
using MMV.Domain.ValueObjects;
using Xunit;

namespace MMV.Domain.Tests.ValueObjectTests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0123456789")]
    [InlineData("01 23 45 67 89")]
    [InlineData("01-23-45-67-89")]
    public void Create_WithValidPhoneNumber_ShouldNormalize(string phone)
    {
        var phoneObj = new PhoneNumber(phone);
        phoneObj.Value.Should().NotContain(" ");
        phoneObj.Value.Should().NotContain("-");
        phoneObj.Value.Length.Should().BeGreaterThanOrEqualTo(6).And.BeLessThanOrEqualTo(20);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("")]
    public void Create_WithInvalidLength_ShouldThrowArgumentException(string phone)
    {
        Assert.Throws<ArgumentException>(() => new PhoneNumber(phone));
    }

    [Fact]
    public void ToString_ShouldReturnNormalizedNumber()
    {
        var phone = new PhoneNumber("01 23 45 67 89");
        var result = phone.ToString();
        result.Should().NotContain(" ");
        result.Should().NotContain("-");
    }

    [Fact]
    public void Create_WithSpacesAndDashes_ShouldNormalizeCorrectly()
    {
        var phone = new PhoneNumber("01 - 23 - 45 - 67 - 89");
        phone.Value.Should().Be("0123456789");
    }
}
