using FluentAssertions;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ValidatorTests;

/// <summary>
/// Tests pour UserValidator (FluentValidation + politique de mot de passe).
/// </summary>
public class UserValidatorTests
{
    private readonly UserValidator _validator = new();

    #region Username validation

    [Fact]
    public void Validate_ValidUser_ShouldPass()
    {
        var user = CreateValidUser();
        var result = _validator.Validate(user);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyUsername_ShouldFail(string? username)
    {
        var user = CreateValidUser();
        user.Username = username!;
        var result = _validator.Validate(user);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Username");
    }

    [Fact]
    public void Validate_ShortUsername_ShouldFail()
    {
        var user = CreateValidUser();
        user.Username = "ab";
        var result = _validator.Validate(user);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Username" && e.ErrorMessage.Contains("3"));
    }

    [Fact]
    public void Validate_UsernameWithSpaces_ShouldFail()
    {
        var user = CreateValidUser();
        user.Username = "jean dupont";
        var result = _validator.Validate(user);
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("jean.dupont")]
    [InlineData("jean-dupont")]
    [InlineData("jean_dupont")]
    [InlineData("JeanDupont123")]
    public void Validate_ValidUsernames_ShouldPass(string username)
    {
        var user = CreateValidUser();
        user.Username = username;
        var result = _validator.Validate(user);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("jean@dupont")]
    [InlineData("jean!dupont")]
    [InlineData("jean dupont")]
    public void Validate_InvalidUsernameCharacters_ShouldFail(string username)
    {
        var user = CreateValidUser();
        user.Username = username;
        var result = _validator.Validate(user);
        result.IsValid.Should().BeFalse();
    }

    #endregion

    #region Name validation

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyFirstName_ShouldFail(string? firstName)
    {
        var user = CreateValidUser();
        user.FirstName = firstName!;
        var result = _validator.Validate(user);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FirstName");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyLastName_ShouldFail(string? lastName)
    {
        var user = CreateValidUser();
        user.LastName = lastName!;
        var result = _validator.Validate(user);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "LastName");
    }

    #endregion

    #region Password policy tests

    [Theory]
    [InlineData("Abcdefg1", true)]
    [InlineData("StrongPass1", true)]
    [InlineData("MyP@ssw0rd", true)]
    public void ValidatePasswordPolicy_ValidPasswords_ShouldPass(string password, bool expected)
    {
        var (isValid, _) = UserValidator.ValidatePasswordPolicy(password);
        isValid.Should().Be(expected);
    }

    [Fact]
    public void ValidatePasswordPolicy_NullPassword_ShouldFail()
    {
        var (isValid, error) = UserValidator.ValidatePasswordPolicy(null);
        isValid.Should().BeFalse();
        error.Should().Contain("requis");
    }

    [Fact]
    public void ValidatePasswordPolicy_TooShort_ShouldFail()
    {
        var (isValid, error) = UserValidator.ValidatePasswordPolicy("Abc1");
        isValid.Should().BeFalse();
        error.Should().Contain("8");
    }

    [Fact]
    public void ValidatePasswordPolicy_NoUppercase_ShouldFail()
    {
        var (isValid, error) = UserValidator.ValidatePasswordPolicy("abcdefg1");
        isValid.Should().BeFalse();
        error.Should().Contain("majuscule");
    }

    [Fact]
    public void ValidatePasswordPolicy_NoLowercase_ShouldFail()
    {
        var (isValid, error) = UserValidator.ValidatePasswordPolicy("ABCDEFG1");
        isValid.Should().BeFalse();
        error.Should().Contain("minuscule");
    }

    [Fact]
    public void ValidatePasswordPolicy_NoDigit_ShouldFail()
    {
        var (isValid, error) = UserValidator.ValidatePasswordPolicy("Abcdefgh");
        isValid.Should().BeFalse();
        error.Should().Contain("chiffre");
    }

    #endregion

    #region Password strength tests

    [Fact]
    public void GetPasswordStrength_EmptyPassword_ReturnsZero()
    {
        UserValidator.GetPasswordStrength("").Should().Be(0);
        UserValidator.GetPasswordStrength(null).Should().Be(0);
    }

    [Fact]
    public void GetPasswordStrength_ShortSimple_ReturnsLow()
    {
        UserValidator.GetPasswordStrength("abc").Should().BeLessThan(3);
    }

    [Fact]
    public void GetPasswordStrength_StrongPassword_ReturnsHigh()
    {
        var strength = UserValidator.GetPasswordStrength("MyStr0ng!Pass123");
        strength.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void GetPasswordStrength_MaxIsFour()
    {
        var strength = UserValidator.GetPasswordStrength("VeryStr0ng!P@ssword2025!!");
        strength.Should().BeLessOrEqualTo(4);
    }

    #endregion

    #region GenerateStrongPassword tests

    [Fact]
    public void GenerateStrongPassword_MeetsPolicy()
    {
        var password = UserValidator.GenerateStrongPassword();
        var (isValid, _) = UserValidator.ValidatePasswordPolicy(password);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void GenerateStrongPassword_RespectsMinLength()
    {
        var password = UserValidator.GenerateStrongPassword(20);
        password.Length.Should().Be(20);
    }

    [Fact]
    public void GenerateStrongPassword_MinimumEight()
    {
        var password = UserValidator.GenerateStrongPassword(4);
        password.Length.Should().BeGreaterOrEqualTo(8);
    }

    #endregion

    private static User CreateValidUser() => new()
    {
        UserId = 1,
        Username = "test.user",
        PasswordHash = "hash",
        FirstName = "Test",
        LastName = "User",
        Role = UserRole.Optician,
        IsActive = true
    };
}
