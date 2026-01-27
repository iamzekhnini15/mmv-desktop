using FluentValidation;
using MMV.Domain.Entities;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ValidatorTests;

public class ProductValidatorTests
{
    private readonly IValidator<Product> _validator = new ProductValidator();

    [Fact]
    public async Task Validate_WithValidProduct_ShouldPass()
    {
        var product = new Product
        {
            Name = "Monture Ray-Ban",
            Reference = "RB3025",
            SalePrice = 150m,
            PurchasePrice = 75m,
            StockQuantity = 50,
            StockAlertThreshold = 5
        };
        var result = await _validator.ValidateAsync(product);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithNullName_ShouldFail()
    {
        var product = new Product
        {
            Name = null!,
            Reference = "REF001",
            SalePrice = 100m,
            PurchasePrice = 50m
        };
        var result = await _validator.ValidateAsync(product);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithNullReference_ShouldFail()
    {
        var product = new Product
        {
            Name = "Verre",
            Reference = null!,
            SalePrice = 100m,
            PurchasePrice = 50m
        };
        var result = await _validator.ValidateAsync(product);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithNegativeSalePrice_ShouldFail()
    {
        var product = new Product
        {
            Name = "Produit",
            Reference = "REF001",
            SalePrice = -10m,
            PurchasePrice = 50m
        };
        var result = await _validator.ValidateAsync(product);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithZeroPrice_ShouldPass()
    {
        var product = new Product
        {
            Name = "Produit",
            Reference = "REF001",
            SalePrice = 0m,
            PurchasePrice = 50m
        };
        var result = await _validator.ValidateAsync(product);
        Assert.True(result.IsValid);
    }
}
