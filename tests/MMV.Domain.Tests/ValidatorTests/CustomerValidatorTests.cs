using FluentValidation;
using MMV.Domain.Entities;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ValidatorTests;

public class CustomerValidatorTests
{
    private readonly IValidator<Customer> _validator = new CustomerValidator();

    [Fact]
    public async Task Validate_WithValidCustomer_ShouldPass()
    {
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "jean.dupont@example.com",
            Phone = "0123456789"
        };
        var result = await _validator.ValidateAsync(customer);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithNullFirstName_ShouldFail()
    {
        var customer = new Customer
        {
            FirstName = null!,
            LastName = "Dupont",
            Email = "jean@example.com"
        };
        var result = await _validator.ValidateAsync(customer);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithNullLastName_ShouldFail()
    {
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = null!,
            Email = "jean@example.com"
        };
        var result = await _validator.ValidateAsync(customer);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithEmptyFirstName_ShouldFail()
    {
        var customer = new Customer
        {
            FirstName = "",
            LastName = "Dupont",
            Email = "jean@example.com"
        };
        var result = await _validator.ValidateAsync(customer);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithInvalidEmail_ShouldFail()
    {
        var customer = new Customer
        {
            FirstName = "Jean",
            LastName = "Dupont",
            Email = "invalid-email"
        };
        var result = await _validator.ValidateAsync(customer);
        Assert.False(result.IsValid);
    }
}
