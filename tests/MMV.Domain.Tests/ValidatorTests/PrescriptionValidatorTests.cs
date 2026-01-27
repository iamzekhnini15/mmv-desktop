using FluentValidation;
using MMV.Domain.Entities;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ValidatorTests;

public class PrescriptionValidatorTests
{
    private readonly IValidator<Prescription> _validator = new PrescriptionValidator();

    [Fact]
    public async Task Validate_WithValidPrescription_ShouldPass()
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.Today,
            OdSphere = 2.5,
            OdCylinder = -1.0,
            OdAxis = 90,
            OgSphere = 2.0,
            OgCylinder = -0.5,
            OgAxis = 180
        };
        var result = await _validator.ValidateAsync(prescription);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithFutureDate_ShouldFail()
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.Today.AddDays(3),
            OdSphere = 2.5,
            OdCylinder = -1.0,
            OdAxis = 90
        };
        var result = await _validator.ValidateAsync(prescription);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithOutOfRangeSphere_ShouldFail()
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.Today,
            OdSphere = 25.0,
            OdCylinder = -1.0,
            OdAxis = 90
        };
        var result = await _validator.ValidateAsync(prescription);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Validate_WithValidRanges_ShouldPass()
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.Today,
            OdSphere = -20.0,
            OdCylinder = -6.0,
            OdAxis = 180,
            OgSphere = 20.0,
            OgCylinder = 0,
            OgAxis = 0
        };
        var result = await _validator.ValidateAsync(prescription);
        Assert.True(result.IsValid);
    }
}
