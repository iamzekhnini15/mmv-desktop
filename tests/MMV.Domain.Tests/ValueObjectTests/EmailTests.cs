using FluentAssertions;
using MMV.Domain.ValueObjects;
using Xunit;

namespace MMV.Domain.Tests.ValueObjectTests;

public class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("john.doe@company.co.uk")]
    public void Create_WithValidEmail_ShouldSucceed(string email)
    {
        var emailObj = new Email(email);
        emailObj.Value.Should().Be(email);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@example.com")]
    public void Create_WithInvalidEmail_ShouldThrowException(string email)
    {
        Assert.Throws<ArgumentException>(() => new Email(email));
    }

    [Fact]
    public void ToString_ShouldReturnEmailAddress()
    {
        var email = new Email("contact@optique.fr");
        email.ToString().Should().Be("contact@optique.fr");
    }

    [Fact]
    public void Equality_ShouldCompareBySameEmail()
    {
        var email1 = new Email("user@example.com");
        var email2 = new Email("user@example.com");
        email1.Should().Be(email2);
    }
}
