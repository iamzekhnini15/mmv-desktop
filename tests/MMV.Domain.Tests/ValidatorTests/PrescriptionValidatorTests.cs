using FluentAssertions;
using FluentValidation;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Validators;
using Xunit;

namespace MMV.Domain.Tests.ValidatorTests;

/// <summary>
/// P3-3B — <see cref="PrescriptionValidator"/>, désormais réellement exécuté par les use cases Create et Update via
/// <c>CommandValidation</c> (il était jusque-là du code mort : aucune plage n'était opposée à l'utilisateur hors de
/// l'UI).
///
/// Couvre, <b>symétriquement pour OD et OG</b> :
/// <list type="number">
///   <item>les plages existantes (non-régression) ;</item>
///   <item>les valeurs finies : <c>NaN</c>, <c>+∞</c> et <c>−∞</c> refusés ;</item>
///   <item>les règles croisées cylindre ⇄ axe ;</item>
///   <item>les règles croisées prisme ⇄ base, <b>sans</b> borne supérieure ;</item>
///   <item>l'ordonnance partielle ou vide, qui reste valide.</item>
/// </list>
///
/// <b>Fixtures historiques amendées sciemment</b> (et non supprimées) : <c>Validate_WithValidRanges_ShouldPass</c>
/// portait <c>OgCylinder = 0</c> avec <c>OgAxis = 0</c> — une combinaison que la règle « cylindre nul ⇒ pas d'axe »
/// déclare désormais incohérente. La fixture est adaptée à la règle, et le cas qu'elle encodait est réaffirmé
/// explicitement (l'axe orphelin est <b>refusé</b>).
/// </summary>
public sealed class PrescriptionValidatorTests
{
    private readonly IValidator<Prescription> _validator = new PrescriptionValidator();

    /// <summary>Les deux yeux, pour prouver la symétrie de chaque règle plutôt que de la supposer.</summary>
    public static TheoryData<string> Eyes => new() { Od, Og };

    private const string Od = "OD";
    private const string Og = "OG";

    /// <summary>
    /// Construit une ordonnance dont <b>un seul</b> œil est renseigné : l'autre reste entièrement vide (ce qui est,
    /// en soi, la preuve que l'ordonnance partielle reste acceptée).
    /// </summary>
    private static Prescription ForEye(
        string eye,
        double? sphere = null,
        double? cylinder = null,
        int? axis = null,
        double? addition = null,
        double? prismValue = null,
        PrismBase? prismBase = null)
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.UtcNow.Date
        };

        if (eye == Od)
        {
            prescription.OdSphere = sphere;
            prescription.OdCylinder = cylinder;
            prescription.OdAxis = axis;
            prescription.OdAddition = addition;
            prescription.OdPrismValue = prismValue;
            prescription.OdPrismBase = prismBase;
        }
        else
        {
            prescription.OgSphere = sphere;
            prescription.OgCylinder = cylinder;
            prescription.OgAxis = axis;
            prescription.OgAddition = addition;
            prescription.OgPrismValue = prismValue;
            prescription.OgPrismBase = prismBase;
        }

        return prescription;
    }

    private bool IsValid(Prescription prescription) => _validator.Validate(prescription).IsValid;

    private IEnumerable<string> MessagesOf(Prescription prescription) =>
        _validator.Validate(prescription).Errors.Select(e => e.ErrorMessage);

    // ------------------------------------------------------------------
    // (1) Plages existantes — non-régression
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_WithValidPrescription_ShouldPass()
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.UtcNow.Date,
            OdSphere = 2.5,
            OdCylinder = -1.0,
            OdAxis = 90,
            OgSphere = 2.0,
            OgCylinder = -0.5,
            OgAxis = 180
        };

        IsValid(prescription).Should().BeTrue();
    }

    [Fact]
    public void Validate_WithFutureDate_ShouldFail()
    {
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.UtcNow.AddDays(3),
            OdSphere = 2.5,
            OdCylinder = -1.0,
            OdAxis = 90
        };

        IsValid(prescription).Should().BeFalse();
    }

    [Fact]
    public void Validate_WithValidRanges_ShouldPass()
    {
        // Bornes exactes. AMENDÉ (P3-3B) : la fixture historique portait OgCylinder = 0 avec OgAxis = 0 — un axe sans
        // cylindre à orienter, désormais refusé (cas réaffirmé en propre ci-dessous). Cylindre nul ⇒ aucun axe.
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.UtcNow.Date,
            OdSphere = -20.0,
            OdCylinder = -6.0,
            OdAxis = 180,
            OgSphere = 20.0,
            OgCylinder = 0,
            OgAxis = null
        };

        IsValid(prescription).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_WithOutOfRangeSphere_ShouldFail(string eye)
    {
        IsValid(ForEye(eye, sphere: 25.0)).Should().BeFalse();
        IsValid(ForEye(eye, sphere: -25.0)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_WithOutOfRangeCylinderOrAddition_ShouldFail(string eye)
    {
        IsValid(ForEye(eye, cylinder: -7.0, axis: 90)).Should().BeFalse();
        IsValid(ForEye(eye, cylinder: 7.0, axis: 90)).Should().BeFalse();
        IsValid(ForEye(eye, addition: 5.0)).Should().BeFalse();
        IsValid(ForEye(eye, addition: -1.0)).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // (2) Valeurs finies — NaN et infinis refusés
    // ------------------------------------------------------------------

    public static TheoryData<string, double> NonFiniteValues
    {
        get
        {
            var data = new TheoryData<string, double>();
            foreach (var eye in new[] { Od, Og })
            {
                data.Add(eye, double.NaN);
                data.Add(eye, double.PositiveInfinity);
                data.Add(eye, double.NegativeInfinity);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Validate_WithNonFiniteSphere_ShouldFail(string eye, double value)
    {
        IsValid(ForEye(eye, sphere: value)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Validate_WithNonFiniteCylinder_ShouldFail(string eye, double value)
    {
        IsValid(ForEye(eye, cylinder: value, axis: 90)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Validate_WithNonFiniteAddition_ShouldFail(string eye, double value)
    {
        IsValid(ForEye(eye, addition: value)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void Validate_WithNonFinitePrismValue_ShouldFail(string eye, double value)
    {
        // PrismValue n'a AUCUNE borne supérieure : sans la règle « valeur finie », rien ne l'aurait protégée.
        var prescription = ForEye(eye, prismValue: value, prismBase: PrismBase.In);

        IsValid(prescription).Should().BeFalse();
        MessagesOf(prescription).Should().Contain(PrescriptionValidator.FiniteValueMessage);
    }

    // ------------------------------------------------------------------
    // (3) Cylindre ⇄ axe
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_CylinderNullOrZero_WithoutAxis_ShouldPass(string eye)
    {
        IsValid(ForEye(eye, cylinder: null, axis: null)).Should().BeTrue();
        IsValid(ForEye(eye, cylinder: 0, axis: null)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_NonZeroCylinder_WithoutAxis_ShouldFail(string eye)
    {
        var prescription = ForEye(eye, cylinder: -1.0, axis: null);

        IsValid(prescription).Should().BeFalse();
        MessagesOf(prescription).Should().Contain(PrescriptionValidator.AxisRequiredMessage);
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_AxisWithoutCylinder_ShouldFail(string eye)
    {
        // Axe orphelin : il n'oriente rien et produirait, à la transposition (P3-6B), une notation fausse.
        var withoutCylinder = ForEye(eye, cylinder: null, axis: 90);
        IsValid(withoutCylinder).Should().BeFalse();
        MessagesOf(withoutCylinder).Should().Contain(PrescriptionValidator.AxisWithoutCylinderMessage);

        // Cylindre explicitement nul = pas d'astigmatisme ⇒ l'axe reste interdit (cas de la fixture amendée).
        var zeroCylinder = ForEye(eye, cylinder: 0, axis: 0);
        IsValid(zeroCylinder).Should().BeFalse();
        MessagesOf(zeroCylinder).Should().Contain(PrescriptionValidator.AxisWithoutCylinderMessage);
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_NonZeroCylinder_WithBoundaryAxis_ShouldPass(string eye)
    {
        // L'axe 0 est ACCEPTÉ en saisie (il sera normalisé à 180 hors du validateur) ; 180 l'est aussi.
        IsValid(ForEye(eye, cylinder: -1.0, axis: 0)).Should().BeTrue();
        IsValid(ForEye(eye, cylinder: -1.0, axis: 180)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_AxisOutOfRange_ShouldFail(string eye)
    {
        IsValid(ForEye(eye, cylinder: -1.0, axis: 181)).Should().BeFalse();
        IsValid(ForEye(eye, cylinder: -1.0, axis: -1)).Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // (4) Prisme ⇄ base
    // ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_NoPrism_ShouldPass(string eye)
    {
        IsValid(ForEye(eye, prismValue: null, prismBase: null)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_PrismValueWithBase_ShouldPass(string eye)
    {
        IsValid(ForEye(eye, prismValue: 2.0, prismBase: PrismBase.In)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_PrismValueWithoutBase_ShouldFail(string eye)
    {
        // C'est le résultat NOMINAL de l'écran actuel (TextBox libre ⇒ base null silencieuse) : P3-3C corrigera l'UI.
        var prescription = ForEye(eye, prismValue: 2.0, prismBase: null);

        IsValid(prescription).Should().BeFalse();
        MessagesOf(prescription).Should().Contain(PrescriptionValidator.PrismBaseRequiredMessage);
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_PrismBaseWithoutValue_ShouldFail(string eye)
    {
        var prescription = ForEye(eye, prismValue: null, prismBase: PrismBase.Out);

        IsValid(prescription).Should().BeFalse();
        MessagesOf(prescription).Should().Contain(PrescriptionValidator.PrismBaseWithoutValueMessage);
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_ZeroPrismValue_MeansNoPrism(string eye)
    {
        // Zéro = absence de prisme (même sémantique que le cylindre nul) : la base doit alors rester vide.
        IsValid(ForEye(eye, prismValue: 0, prismBase: null)).Should().BeTrue();

        var withBase = ForEye(eye, prismValue: 0, prismBase: PrismBase.Up);
        IsValid(withBase).Should().BeFalse();
        MessagesOf(withBase).Should().Contain(PrescriptionValidator.PrismBaseWithoutValueMessage);
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_NegativePrismValue_ShouldFail(string eye)
    {
        var prescription = ForEye(eye, prismValue: -1.0, prismBase: PrismBase.Down);

        IsValid(prescription).Should().BeFalse();
        MessagesOf(prescription).Should().Contain(PrescriptionValidator.PrismValueNegativeMessage);
    }

    [Theory]
    [MemberData(nameof(Eyes))]
    public void Validate_LargeFinitePrismValue_ShouldPass(string eye)
    {
        // AUCUNE borne supérieure n'est imposée (décision P3-3A : pas de limite clinique inventée). Ce test verrouille
        // cette décision : introduire un plafond devra être un choix conscient, pas un effet de bord.
        IsValid(ForEye(eye, prismValue: 50.0, prismBase: PrismBase.In)).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // (5) Ordonnance partielle ou vide
    // ------------------------------------------------------------------

    [Fact]
    public void Validate_WithOnlyRightEye_ShouldPass()
    {
        IsValid(ForEye(Od, sphere: -1.25, cylinder: -0.5, axis: 90, addition: 1.0)).Should().BeTrue();
    }

    [Fact]
    public void Validate_WithOnlyLeftEye_ShouldPass()
    {
        IsValid(ForEye(Og, sphere: -1.25, cylinder: -0.5, axis: 90, addition: 1.0)).Should().BeTrue();
    }

    [Fact]
    public void Validate_WithNoOpticalValueAtAll_ShouldPass()
    {
        // Iso-comportement assumé (P3-3A) : aucune règle « au moins une valeur optique » n'est ajoutée en P3-3B.
        var empty = new Prescription { CustomerId = 1, IssueDate = DateTime.UtcNow.Date };

        IsValid(empty).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithoutDoctorName_ShouldPass(string? doctorName)
    {
        // DoctorName reste FACULTATIF dans le Domain (P3-3B) ; c'est l'UI qui l'exige à tort — alignement en P3-3C.
        var prescription = new Prescription
        {
            CustomerId = 1,
            IssueDate = DateTime.UtcNow.Date,
            DoctorName = doctorName
        };

        IsValid(prescription).Should().BeTrue();
    }
}
