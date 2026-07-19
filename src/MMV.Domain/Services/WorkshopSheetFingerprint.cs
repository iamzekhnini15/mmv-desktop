using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MMV.Domain.Entities;

namespace MMV.Domain.Services;

/// <summary>
/// Empreinte <b>déterministe</b> des données <b>techniques</b> d'une commande (P3-6B) : elle permet de détecter
/// qu'une fiche atelier est devenue <b>obsolète</b> parce que la commande a changé depuis sa génération.
/// </summary>
/// <remarks>
/// <para>
/// <b>Rôle exact.</b> L'empreinte est un <i>filet de sécurité en lecture</i> : calculée au snapshot, stockée sur
/// la fiche, puis recalculée depuis la commande réelle pour comparaison. Elle n'est <b>jamais</b> une clé métier,
/// ne participe à <b>aucune</b> contrainte d'unicité et n'est jamais affichée.
/// </para>
/// <para>
/// <b>Périmètre — inclus.</b> Les instructions de la commande (<see cref="Order.Notes"/>, affichées comme
/// consignes d'atelier) et, pour chaque ligne : type, produit source, quantité, usage, sphère, cylindre, axe,
/// addition, prisme, base du prisme, acuité visuelle.
/// </para>
/// <para>
/// <b>Périmètre — exclus délibérément.</b> Téléphone, email, montants (une fiche n'est ni facture ni devis), le
/// nom <i>actuel</i> du produit relu depuis le catalogue (un renommage de catalogue ne remet pas en cause une
/// fabrication), le statut de la commande et l'état du contrôle qualité (ils évoluent par nature — les inclure
/// rendrait toute fiche obsolète dès la transition suivante).
/// </para>
/// <para>
/// <b>Canonicalisation (indispensable à l'injectivité).</b> Ordre de propriétés fixe ; lignes triées par
/// <c>OrderItemId</c> avec un ordre de secours explicite (type, produit, quantité) pour les lignes non encore
/// persistées ; <see cref="double"/> au format aller-retour <c>"R"</c> en culture invariante (jamais <c>F2</c>,
/// qui écraserait une différence réelle) ; entiers en culture invariante ; enums par leur nom stable ; marqueur
/// de nul <b>distinct</b> d'une chaîne vide ; chaînes <b>préfixées de leur longueur</b>, ce qui rend l'encodage
/// injectif et interdit toute collision par caractère séparateur contenu dans un texte.
/// </para>
/// </remarks>
public static class WorkshopSheetFingerprint
{
    /// <summary>
    /// Version du format d'empreinte. Un changement de contenu ou d'encodage <b>doit</b> incrémenter ce tag :
    /// les empreintes d'anciennes fiches deviennent alors mécaniquement différentes (fiches signalées obsolètes)
    /// plutôt que faussement identiques.
    /// </summary>
    private const string FormatTag = "WSFP1";

    /// <summary>Marqueur d'absence de valeur, distinct de toute chaîne vide encodée (<c>s0:</c>).</summary>
    private const string NullMarker = "~";

    /// <summary>
    /// Calcule l'empreinte technique de <paramref name="order"/> et de ses lignes.
    /// </summary>
    /// <exception cref="ArgumentNullException">si <paramref name="order"/> est <c>null</c>.</exception>
    public static string Compute(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        var builder = new StringBuilder();
        builder.Append(FormatTag);

        // Bloc commande : uniquement les instructions d'atelier (le reste de l'en-tête n'est pas technique).
        AppendString(builder, order.Notes);

        // Lignes : ordre métier déterministe, indépendant de l'ordre de restitution d'EF.
        var items = (order.OrderItems ?? new List<OrderItem>())
            .OrderBy(i => i.OrderItemId)
            .ThenBy(i => (int)i.ItemType)
            .ThenBy(i => i.ProductId ?? long.MinValue)
            .ThenBy(i => i.Quantity);

        foreach (var item in items)
        {
            AppendEnum(builder, item.ItemType);
            AppendNullableLong(builder, item.ProductId);
            AppendInt(builder, item.Quantity);
            AppendNullableEnum(builder, item.UsageType);
            AppendNullableDouble(builder, item.Sphere);
            AppendNullableDouble(builder, item.Cylinder);
            AppendNullableInt(builder, item.Axis);
            AppendNullableDouble(builder, item.Addition);
            AppendNullableDouble(builder, item.PrismValue);
            AppendNullableEnum(builder, item.PrismBase);
            AppendString(builder, item.VisualAcuity);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Chaîne encodée <b>préfixée de sa longueur</b> (<c>s{n}:{valeur}</c>) : deux découpages différents ne
    /// peuvent pas produire la même séquence, quel que soit le contenu (y compris des séparateurs).
    /// </summary>
    private static void AppendString(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            builder.Append(NullMarker);
            return;
        }

        builder.Append('s').Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value);
    }

    private static void AppendInt(StringBuilder builder, int value)
        => builder.Append('i').Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');

    private static void AppendNullableInt(StringBuilder builder, int? value)
    {
        if (value is null)
        {
            builder.Append(NullMarker);
            return;
        }

        AppendInt(builder, value.Value);
    }

    private static void AppendNullableLong(StringBuilder builder, long? value)
    {
        if (value is null)
        {
            builder.Append(NullMarker);
            return;
        }

        builder.Append('l').Append(value.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
    }

    /// <summary>
    /// <see cref="double"/> au format aller-retour <c>"R"</c> : conserve la valeur exacte, donc une différence
    /// réelle de correction produit toujours une empreinte différente (contrairement à un format arrondi).
    /// </summary>
    private static void AppendNullableDouble(StringBuilder builder, double? value)
    {
        if (value is null)
        {
            builder.Append(NullMarker);
            return;
        }

        builder.Append('d').Append(value.Value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
    }

    private static void AppendEnum<TEnum>(StringBuilder builder, TEnum value) where TEnum : struct, Enum
        => builder.Append('e').Append(value.ToString()).Append(';');

    private static void AppendNullableEnum<TEnum>(StringBuilder builder, TEnum? value) where TEnum : struct, Enum
    {
        if (value is null)
        {
            builder.Append(NullMarker);
            return;
        }

        AppendEnum(builder, value.Value);
    }
}
