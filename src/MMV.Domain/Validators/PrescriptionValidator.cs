using System.Linq.Expressions;
using FluentValidation;
using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Validators;

/// <summary>
/// Règles de validation d'une ordonnance — <b>propriétaire unique</b> des règles optiques (ADR frontières §9).
/// Exécuté par <c>CreatePrescriptionUseCase</c> et <c>UpdatePrescriptionUseCase</c> via <c>CommandValidation</c>
/// (P3-3B) : jusque-là ce validateur n'était appelé par personne, et aucune plage n'était réellement opposée à
/// l'utilisateur en dehors de l'UI.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ce validateur valide, il ne normalise pas.</b> Un axe de <c>0</c> est <b>accepté</b> ici ; sa mise sous forme
/// canonique (<c>0 → 180</c>) appartient à <see cref="Optics.OpticalAxisNormalizer"/>, appelé par les use cases
/// après la validation. Aucune mutation de commande ni d'entité n'a lieu pendant la validation.
/// </para>
/// <para>
/// <b>Valeurs finies d'abord.</b> Toute valeur optique <c>double?</c> renseignée est d'abord vérifiée <b>finie</b>
/// (<c>NaN</c> et infinis refusés) ; les plages ne s'appliquent qu'ensuite. Une comparaison de plage avec <c>NaN</c>
/// est toujours fausse et produirait un message trompeur, et <c>PrismValue</c> — qui n'a <b>volontairement aucune
/// borne supérieure</b> (aucune preuve métier, cf. P3-3A) — ne serait sinon protégée par rien.
/// </para>
/// <para>
/// <b>Ordonnance partielle préservée.</b> Aucun champ optique n'est rendu obligatoire : un œil entièrement vide, un
/// seul œil renseigné, voire une ordonnance vide, restent valides. Les règles croisées sont <b>conditionnelles à la
/// présence</b> des champs, et strictement symétriques entre OD et OG.
/// </para>
/// </remarks>
public class PrescriptionValidator : AbstractValidator<Prescription>
{
    // Messages métier stables, exposés en constantes pour que les tests — et l'UI de P3-3C — s'y réfèrent sans
    // dupliquer de littéral (patron P3-2B : DeleteCustomerUseCase.CustomerHasHistoryMessage).

    public const string AxisRequiredMessage =
        "L'axe est obligatoire lorsqu'un cylindre non nul est renseigné.";

    public const string AxisWithoutCylinderMessage =
        "L'axe ne peut être renseigné que si un cylindre non nul est saisi.";

    public const string PrismBaseRequiredMessage =
        "La base du prisme est obligatoire lorsqu'une valeur de prisme est renseignée.";

    public const string PrismBaseWithoutValueMessage =
        "La base du prisme ne peut être renseignée que si la valeur du prisme est strictement positive.";

    public const string PrismValueNegativeMessage =
        "La valeur du prisme ne peut pas être négative.";

    public const string FiniteValueMessage =
        "La valeur doit être un nombre fini.";

    public PrescriptionValidator()
    {
        // Borne évaluée à CHAQUE validation (lambda) et non figée à la construction : une instance partagée ou
        // enregistrée en singleton aurait sinon gelé la date limite au démarrage du processus.
        //
        // P4-5D : IssueDate est une DATE CIVILE (DateOnly), plus un instant. La borne se calcule donc en
        // dates civiles, et la tolérance « +1 jour » garde exactement le sens qu'elle avait : absorber
        // l'écart de fuseau entre le poste de saisie et la référence, sans autoriser une ordonnance
        // franchement future. Le validateur ne reçoit pas IClock : FluentValidation construit ses règles
        // sans conteneur, et cette borne n'est pas un horodatage métier persisté — c'est une garde de
        // saisie (ADR-PROD-DB-004 §5, décisions 2 et 7).
        RuleFor(p => p.IssueDate)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1));

        AddEyeRules(
            p => p.OdSphere, p => p.OdCylinder, p => p.OdAxis,
            p => p.OdAddition, p => p.OdPrismValue, p => p.OdPrismBase);

        AddEyeRules(
            p => p.OgSphere, p => p.OgCylinder, p => p.OgAxis,
            p => p.OgAddition, p => p.OgPrismValue, p => p.OgPrismBase);
    }

    /// <summary>
    /// Applique à un œil l'intégralité des règles optiques. Appelée une fois pour OD et une fois pour OG : la
    /// symétrie des deux yeux est garantie par construction, non par recopie.
    /// </summary>
    private void AddEyeRules(
        Expression<Func<Prescription, double?>> sphere,
        Expression<Func<Prescription, double?>> cylinder,
        Expression<Func<Prescription, int?>> axis,
        Expression<Func<Prescription, double?>> addition,
        Expression<Func<Prescription, double?>> prismValue,
        Expression<Func<Prescription, PrismBase?>> prismBase)
    {
        var sphereOf = sphere.Compile();
        var cylinderOf = cylinder.Compile();
        var axisOf = axis.Compile();
        var additionOf = addition.Compile();
        var prismValueOf = prismValue.Compile();

        // --- Valeurs finies : NaN, +∞ et −∞ refusés sur toute valeur renseignée.
        RuleFor(sphere).Must(IsFiniteOrNull).WithMessage(FiniteValueMessage);
        RuleFor(cylinder).Must(IsFiniteOrNull).WithMessage(FiniteValueMessage);
        RuleFor(addition).Must(IsFiniteOrNull).WithMessage(FiniteValueMessage);
        RuleFor(prismValue).Must(IsFiniteOrNull).WithMessage(FiniteValueMessage);

        // --- Plages, appliquées seulement à une valeur finie.
        RuleFor(sphere).InclusiveBetween(-20, 20).When(p => IsFinite(sphereOf(p)));
        RuleFor(cylinder).InclusiveBetween(-6, 6).When(p => IsFinite(cylinderOf(p)));
        RuleFor(addition).InclusiveBetween(0, 4).When(p => IsFinite(additionOf(p)));
        RuleFor(axis).InclusiveBetween(0, 180).When(p => axisOf(p).HasValue);

        // --- Cylindre ⇄ axe : un axe n'oriente que ce qui existe. Sans cylindre à orienter, il n'a aucun sens et
        // deviendrait, à la transposition (P3-6B), une notation fausse.
        RuleFor(axis)
            .NotNull()
            .When(p => HasOrientableCylinder(cylinderOf(p)))
            .WithMessage(AxisRequiredMessage);

        RuleFor(axis)
            .Null()
            .When(p => !HasOrientableCylinder(cylinderOf(p)))
            .WithMessage(AxisWithoutCylinderMessage);

        // --- Prisme ⇄ base : zéro = absence de prisme (même sémantique que le cylindre nul).
        RuleFor(prismValue)
            .GreaterThanOrEqualTo(0)
            .When(p => IsFinite(prismValueOf(p)))
            .WithMessage(PrismValueNegativeMessage);

        RuleFor(prismBase)
            .NotNull()
            .When(p => HasPrism(prismValueOf(p)))
            .WithMessage(PrismBaseRequiredMessage);

        RuleFor(prismBase)
            .Null()
            .When(p => !HasPrism(prismValueOf(p)))
            .WithMessage(PrismBaseWithoutValueMessage);
    }

    private static bool IsFiniteOrNull(double? value) => !value.HasValue || double.IsFinite(value.Value);

    private static bool IsFinite(double? value) => value.HasValue && double.IsFinite(value.Value);

    /// <summary>
    /// Un cylindre « orientable » est une valeur finie <b>différente de zéro</b> : <c>0</c> signifie explicitement
    /// « pas d'astigmatisme » et n'appelle donc aucun axe. Les valeurs optiques étant saisies au quart de dioptrie
    /// (fractions binaires exactes), la comparaison <c>!= 0</c> est fiable : aucune tolérance arbitraire n'est
    /// introduite.
    /// </summary>
    private static bool HasOrientableCylinder(double? cylinder) => IsFinite(cylinder) && cylinder!.Value != 0d;

    /// <summary>Un prisme existe si sa valeur est finie et <b>strictement positive</b> ; <c>0</c> = pas de prisme.</summary>
    private static bool HasPrism(double? prismValue) => IsFinite(prismValue) && prismValue!.Value > 0d;
}
