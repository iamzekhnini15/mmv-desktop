using FluentAssertions;
using MMV.Domain.Optics;
using Xunit;

namespace MMV.Domain.Tests.OpticsTests;

/// <summary>
/// P3-3B — <see cref="OpticalAxisNormalizer"/> : fonction Domain <b>pure</b> (sans dépôt, sans état, sans effet de
/// bord) qui produit l'écriture canonique de l'axe avant persistance. <c>0°</c> et <c>180°</c> désignent la même
/// orientation ; le dépôt retient <c>180</c>.
///
/// La normalisation vit <b>hors</b> du validateur (FluentValidation valide, ne mute pas) : ces tests l'appellent donc
/// directement.
/// </summary>
public sealed class OpticalAxisNormalizerTests
{
    [Fact]
    public void NormalizeAxis_Null_StaysNull()
    {
        // Pas d'axe ⇒ aucune valeur n'est inventée (une ordonnance sans cylindre n'a pas d'axe).
        OpticalAxisNormalizer.NormalizeAxis(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeAxis_Zero_BecomesOneHundredEighty()
    {
        OpticalAxisNormalizer.NormalizeAxis(0).Should().Be(180);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(90)]
    [InlineData(180)]
    public void NormalizeAxis_WithinCanonicalRange_IsUnchanged(int axis)
    {
        OpticalAxisNormalizer.NormalizeAxis(axis).Should().Be(axis);
    }
}
