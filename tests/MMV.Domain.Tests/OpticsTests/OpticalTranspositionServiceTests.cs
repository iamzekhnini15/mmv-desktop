using FluentAssertions;
using MMV.Domain.Optics;
using Xunit;

namespace MMV.Domain.Tests.OpticsTests;

/// <summary>
/// P3-6B — <see cref="OpticalTranspositionService"/> : transposition optique <b>pure</b>.
///
/// Prouve que le service (1) applique exactement la formule métier, (2) ne fabrique jamais de donnée absente,
/// (3) est une involution à la convention d'axe près, et (4) ne peut structurellement pas modifier sa source.
/// </summary>
public sealed class OpticalTranspositionServiceTests
{
    // -------------------------------------------------------------------------------------------------------
    // Formule
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Transpose_ExempleDeReference_DonneLaNotationAttendue()
    {
        // +2.00 (−1.00) axe 180  ⇔  +1.00 (+1.00) axe 90 (notes domaine §3.3).
        var result = OpticalTranspositionService.Transpose(new OpticalCorrection(2.00, -1.00, 180));

        result.IsTransposed.Should().BeTrue();
        result.Correction.Sphere.Should().Be(1.00);
        result.Correction.Cylinder.Should().Be(1.00);
        result.Correction.Axis.Should().Be(90);
    }

    [Fact]
    public void Transpose_CylindrePositif_InverseLeSigneEtAjouteALaSphere()
    {
        var result = OpticalTranspositionService.Transpose(new OpticalCorrection(-1.50, 0.75, 20));

        result.IsTransposed.Should().BeTrue();
        result.Correction.Sphere.Should().Be(-0.75);
        result.Correction.Cylinder.Should().Be(-0.75);
        result.Correction.Axis.Should().Be(110);
    }

    [Fact]
    public void Transpose_CylindreNegatif_InverseLeSigneEtAjouteALaSphere()
    {
        var result = OpticalTranspositionService.Transpose(new OpticalCorrection(3.25, -2.25, 45));

        result.IsTransposed.Should().BeTrue();
        result.Correction.Sphere.Should().Be(1.00);
        result.Correction.Cylinder.Should().Be(2.25);
        result.Correction.Axis.Should().Be(135);
    }

    // -------------------------------------------------------------------------------------------------------
    // Bornes d'axe
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(10, 100)]   // axe < 90 : simple rotation
    [InlineData(89, 179)]
    [InlineData(90, 180)]   // exactement 90 → 180 (pas de retranchement : 180 n'est pas > 180)
    [InlineData(91, 1)]     // au-delà : retranchement d'un demi-tour
    [InlineData(135, 45)]   // axe > 90
    [InlineData(180, 90)]   // axe 180 → 90
    [InlineData(0, 90)]     // axe 0 → 90
    public void Transpose_AxeToujoursRameneDansLeDomaineUtile(int sourceAxis, int expectedAxis)
    {
        var result = OpticalTranspositionService.Transpose(new OpticalCorrection(1.00, -1.00, sourceAxis));

        result.IsTransposed.Should().BeTrue();
        result.Correction.Axis.Should().Be(expectedAxis);
    }

    [Fact]
    public void Transpose_NeProduitJamaisUnAxeZero()
    {
        // La convention canonique du dépôt (P3-3B) est ]0, 180] : un axe transposé de 0 serait une seconde
        // écriture du même axe.
        for (var axis = 0; axis <= 180; axis++)
        {
            var result = OpticalTranspositionService.Transpose(new OpticalCorrection(0.50, -0.25, axis));

            result.Correction.Axis.Should().NotBe(0);
            result.Correction.Axis.Should().BeInRange(1, 180);
        }
    }

    // -------------------------------------------------------------------------------------------------------
    // Cas « rien à transposer » / « non transposable » — aucune donnée inventée
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Transpose_CylindreAbsent_RenvoieLaSourceInchangeeSansTransposition()
    {
        var source = new OpticalCorrection(-2.00, null, null);

        var result = OpticalTranspositionService.Transpose(source);

        result.IsTransposed.Should().BeFalse();
        result.Correction.Should().Be(source);
    }

    [Fact]
    public void Transpose_CylindreNul_EstUneIdentite()
    {
        // 0 = « pas d'astigmatisme » (même sémantique que PrescriptionValidator.HasOrientableCylinder).
        var source = new OpticalCorrection(1.25, 0d, null);

        var result = OpticalTranspositionService.Transpose(source);

        result.IsTransposed.Should().BeFalse();
        result.Correction.Should().Be(source);
    }

    [Fact]
    public void Transpose_CylindreNonNulSansAxe_NInventeAucunAxe()
    {
        // Donnée déjà incohérente en amont (P3-3B exige un axe si le cylindre est orientable).
        // La transposition ne la corrige pas et surtout ne fabrique pas d'axe.
        var source = new OpticalCorrection(1.00, -1.00, null);

        var result = OpticalTranspositionService.Transpose(source);

        result.IsTransposed.Should().BeFalse();
        result.Correction.Should().Be(source);
        result.Correction.Axis.Should().BeNull();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Transpose_CylindreNonFini_NEstPasTranspose(double cylinder)
    {
        var source = new OpticalCorrection(1.00, cylinder, 90);

        var result = OpticalTranspositionService.Transpose(source);

        result.IsTransposed.Should().BeFalse();
        result.Correction.Should().Be(source);
    }

    [Fact]
    public void Transpose_SphereAbsente_ResteAbsente()
    {
        // On n'invente pas une sphère plan (0.00) : une sphère non renseignée le reste.
        var result = OpticalTranspositionService.Transpose(new OpticalCorrection(null, -1.00, 90));

        result.IsTransposed.Should().BeTrue();
        result.Correction.Sphere.Should().BeNull();
        result.Correction.Cylinder.Should().Be(1.00);
        result.Correction.Axis.Should().Be(180);
    }

    // -------------------------------------------------------------------------------------------------------
    // Involution
    // -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(2.00, -1.00, 180, 180)]  // axe 180 reste 180
    [InlineData(-1.50, 0.75, 20, 20)]
    [InlineData(3.25, -2.25, 45, 45)]
    [InlineData(0.00, -0.25, 90, 90)]
    [InlineData(1.00, -1.00, 0, 180)]    // axe 0 revient sous sa forme CANONIQUE 180
    public void Transpose_AppliqueeDeuxFois_RedonneLaCorrectionCanoniqueInitiale(
        double sphere, double cylinder, int axis, int expectedCanonicalAxis)
    {
        var source = new OpticalCorrection(sphere, cylinder, axis);

        var once = OpticalTranspositionService.Transpose(source);
        var twice = OpticalTranspositionService.Transpose(once.Correction);

        twice.IsTransposed.Should().BeTrue();
        twice.Correction.Sphere.Should().Be(sphere);
        twice.Correction.Cylinder.Should().Be(cylinder);
        twice.Correction.Axis.Should().Be(expectedCanonicalAxis);
    }

    [Fact]
    public void Transpose_ResteExacteAuQuartDeDioptrie_SansArrondi()
    {
        // Les valeurs au quart de dioptrie sont des fractions binaires exactes : aucune dérive ne doit apparaître,
        // et aucun arrondi ne doit être introduit par le service.
        var result = OpticalTranspositionService.Transpose(new OpticalCorrection(-6.75, -0.25, 175));

        result.Correction.Sphere.Should().Be(-7.00);
        result.Correction.Cylinder.Should().Be(0.25);
        result.Correction.Axis.Should().Be(85);
    }

    // -------------------------------------------------------------------------------------------------------
    // Source jamais modifiée
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Transpose_NeModifieJamaisLaCorrectionSource()
    {
        var source = new OpticalCorrection(2.00, -1.00, 180);

        OpticalTranspositionService.Transpose(source);

        source.Sphere.Should().Be(2.00);
        source.Cylinder.Should().Be(-1.00);
        source.Axis.Should().Be(180);
    }

    [Fact]
    public void OpticalCorrection_NePorteQueSphereCylindreAxe()
    {
        // Garantie STRUCTURELLE : addition, prisme, base et acuité ne sont pas exposés au service de
        // transposition — il lui est donc impossible de les altérer. Ce test verrouille cette frontière.
        var properties = typeof(OpticalCorrection)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        properties.Should().BeEquivalentTo(new[] { nameof(OpticalCorrection.Sphere), nameof(OpticalCorrection.Cylinder), nameof(OpticalCorrection.Axis) });
    }

    [Fact]
    public void OpticalTranspositionService_EstPur_SansDependance()
    {
        // Un service de transposition qui dépendrait d'un dépôt pourrait écrire : on verrouille l'absence de
        // toute dépendance (classe statique sans constructeur d'instance, sans champ d'instance).
        var type = typeof(OpticalTranspositionService);

        type.IsAbstract.Should().BeTrue();
        type.IsSealed.Should().BeTrue(); // static class = abstract + sealed
        type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Should().BeEmpty();
    }
}
