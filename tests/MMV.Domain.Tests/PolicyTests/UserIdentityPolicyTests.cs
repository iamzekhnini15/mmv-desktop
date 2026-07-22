using FluentAssertions;
using MMV.Domain.Policies;
using Xunit;

namespace MMV.Domain.Tests.PolicyTests;

/// <summary>
/// P3-10 — Propriétaire unique de la normalisation de l'identifiant de connexion.
/// </summary>
public class UserIdentityPolicyTests
{
    [Theory]
    [InlineData("admin", "admin")]
    [InlineData("Admin", "admin")]
    [InlineData("ADMIN", "admin")]
    [InlineData("  ADMIN  ", "admin")]
    [InlineData("Marie.Optic", "marie.optic")]
    [InlineData("PIERRE_TECH", "pierre_tech")]
    [InlineData("jean-luc", "jean-luc")]
    public void NormalizeUsername_TrimsAndLowercases(string input, string expected)
        => UserIdentityPolicy.NormalizeUsername(input).Should().Be(expected);

    /// <summary>
    /// Les variantes de casse et d'espacement d'un même login produisent la MÊME clé : c'est la propriété dont
    /// dépend l'unicité en base.
    /// </summary>
    [Fact]
    public void NormalizeUsername_CaseVariants_ProduceIdenticalKey()
    {
        var keys = new[] { "admin", "Admin", "ADMIN", " Admin ", "aDmIn" }
            .Select(UserIdentityPolicy.NormalizeUsername)
            .Distinct()
            .ToList();

        keys.Should().ContainSingle().Which.Should().Be("admin");
    }

    /// <summary>
    /// <c>null</c> et blanc produisent une chaîne vide plutôt qu'une exception : une entrée manquante reste une
    /// erreur de SAISIE, refusée en aval par <c>UserValidator</c> avec un message lisible (convention P3-1).
    /// Lever ici transformerait une saisie invalide en panne technique.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeUsername_NullOrBlank_ReturnsEmpty_WithoutThrowing(string? input)
        => UserIdentityPolicy.NormalizeUsername(input).Should().BeEmpty();

    /// <summary>
    /// Idempotence : normaliser une valeur déjà normalisée ne la change pas. Sans cette propriété, un
    /// enregistrement relu puis réécrit pourrait dériver.
    /// </summary>
    [Theory]
    [InlineData("Admin")]
    [InlineData("  Marie.Optic  ")]
    public void NormalizeUsername_IsIdempotent(string input)
    {
        var once = UserIdentityPolicy.NormalizeUsername(input);
        UserIdentityPolicy.NormalizeUsername(once).Should().Be(once);
    }

    [Fact]
    public void UsernameMaxLength_MatchesValidatorAndSchema()
        => UserIdentityPolicy.UsernameMaxLength.Should().Be(50);
}
