using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Optics;
using MMV.Domain.Validators;

namespace MMV.Domain.Services;

/// <summary>
/// Données optiques <b>validées et canoniques</b> d'une ligne verre (P3-7), telles qu'elles seront recopiées dans
/// <c>SaleItem</c> (et, le cas échéant, dans l'<c>OrderItem</c> de commande fournisseur).
/// </summary>
public readonly record struct SaleLineOptics(
    double? Sphere,
    double? Cylinder,
    int? Axis,
    double? Addition,
    double? PrismValue,
    PrismBase? PrismBase);

/// <summary>
/// <b>Propriétaire unique</b> des invariants optiques d'une ligne de vente (P3-7). Politique Domain <b>pure</b> :
/// aucun dépôt, aucun état, aucun effet de bord.
/// </summary>
/// <remarks>
/// <para>
/// <b>Périmètre volontairement minimal.</b> Seuls les invariants <b>déjà établis par P3-3</b> sont opposés, et
/// seulement aux lignes <see cref="OrderItemType.LensOd"/> / <see cref="OrderItemType.LensOg"/> : valeurs finies,
/// cohérence cylindre ⇄ axe, cohérence prisme ⇄ base, plage d'axe, et mise sous forme canonique de l'axe. Aucune
/// ordonnance complète n'est exigée — les champs optiques restent <b>facultatifs</b>, exactement comme dans
/// <see cref="PrescriptionValidator"/>.
/// </para>
/// <para>
/// <b>Messages réutilisés, non dupliqués.</b> Les messages proviennent littéralement des constantes de
/// <see cref="PrescriptionValidator"/> : une même incohérence optique produit le même texte, qu'elle soit saisie
/// sur une ordonnance (P3-3B) ou sur une ligne de vente (P3-7).
/// </para>
/// <para>
/// <b>Aucun lien avec une ordonnance.</b> Les données optiques d'une vente sont un <b>instantané copié</b> : il
/// n'existe aucune clé étrangère <c>PrescriptionId</c>, et cette politique ne lit ni ne modifie jamais une
/// ordonnance existante. Supprimer ou corriger une ordonnance laisse la vente inchangée — comportement d'origine,
/// confirmé correct par l'audit §9.11 et conservé tel quel.
/// </para>
/// </remarks>
public static class SaleLineOpticsPolicy
{
    /// <summary>Message opposé à un axe hors de la plage réellement admise par le dépôt (<c>[0, 180]</c>).</summary>
    public const string AxisOutOfRangeMessage =
        "L'axe doit être compris entre 0 et 180 degrés.";

    /// <summary>
    /// Valide les invariants optiques d'une ligne, puis renvoie ses données sous forme <b>canonique</b>
    /// (axe <c>0 → 180</c>). Les lignes non-verre sont renvoyées inchangées : aucune règle optique ne leur est
    /// opposée (comportement d'origine conservé).
    /// </summary>
    /// <exception cref="BusinessRuleException">pour toute violation d'invariant optique.</exception>
    public static SaleLineOptics ValidateAndNormalize(OrderItemType itemType, SaleLineOptics optics)
    {
        if (itemType != OrderItemType.LensOd && itemType != OrderItemType.LensOg)
            return optics;

        // --- Valeurs finies d'abord : NaN et infinis refusés. Toute comparaison de plage avec NaN étant fausse,
        // la vérifier ensuite produirait un message trompeur (même ordre que PrescriptionValidator).
        RequireFinite(optics.Sphere);
        RequireFinite(optics.Cylinder);
        RequireFinite(optics.Addition);
        RequireFinite(optics.PrismValue);

        // --- Cylindre ⇄ axe : un axe n'oriente que ce qui existe ; un cylindre non nul doit être orienté.
        var hasOrientableCylinder = IsFinite(optics.Cylinder) && optics.Cylinder!.Value != 0d;

        if (hasOrientableCylinder && !optics.Axis.HasValue)
            throw new BusinessRuleException(PrescriptionValidator.AxisRequiredMessage);

        if (!hasOrientableCylinder && optics.Axis.HasValue)
            throw new BusinessRuleException(PrescriptionValidator.AxisWithoutCylinderMessage);

        if (optics.Axis is < 0 or > 180)
            throw new BusinessRuleException(AxisOutOfRangeMessage);

        // --- Prisme ⇄ base : zéro = absence de prisme (même sémantique que le cylindre nul).
        if (optics.PrismValue is < 0d)
            throw new BusinessRuleException(PrescriptionValidator.PrismValueNegativeMessage);

        var hasPrism = IsFinite(optics.PrismValue) && optics.PrismValue!.Value > 0d;

        if (hasPrism && optics.PrismBase is null)
            throw new BusinessRuleException(PrescriptionValidator.PrismBaseRequiredMessage);

        if (!hasPrism && optics.PrismBase is not null)
            throw new BusinessRuleException(PrescriptionValidator.PrismBaseWithoutValueMessage);

        // Valeur canonique produite APRÈS la validation (l'axe 0 est accepté en saisie, persisté à 180), via le
        // normaliseur déjà propriétaire de la convention produit (P3-3B).
        return optics with { Axis = OpticalAxisNormalizer.NormalizeAxis(optics.Axis) };
    }

    private static void RequireFinite(double? value)
    {
        if (value.HasValue && !double.IsFinite(value.Value))
            throw new BusinessRuleException(PrescriptionValidator.FiniteValueMessage);
    }

    private static bool IsFinite(double? value) => value.HasValue && double.IsFinite(value.Value);
}
