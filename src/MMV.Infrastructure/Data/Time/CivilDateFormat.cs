using System.Globalization;
using System.Text.RegularExpressions;

namespace MMV.Infrastructure.Data.Time;

/// <summary>
/// État d'une valeur de date civile stockée (<c>TEXT</c> SQLite) vis-à-vis du modèle courant, qui attend
/// <see cref="DateOnly"/> depuis P4-5D.
/// </summary>
public enum CivilDateFormatState
{
    /// <summary>Valeur <c>NULL</c> : rien à lire, rien à reprendre (colonne nullable uniquement).</summary>
    NotApplicable,

    /// <summary>
    /// Forme cible exacte <c>yyyy-MM-dd</c>. Lue sans erreur, déjà canonique : <b>aucune action</b>.
    /// </summary>
    Canonical,

    /// <summary>
    /// <b>Lue sans erreur</b> par le modèle courant mais pas sous la forme canonique, et une troncature à
    /// dix caractères redonne <b>exactement la même date</b>. La réécriture est donc une simple mise en
    /// forme, <b>sans perte ni changement de valeur</b> (ex. <c>« 1985-03-15T12:30:45.123 »</c>).
    /// </summary>
    NormalizableReadable,

    /// <summary>
    /// <b>Lue sans erreur</b>, non canonique, et la troncature ne redonnerait <b>pas</b> la même date (ou
    /// n'est pas applicable). Elle est donc <b>laissée intacte</b> et signalée : elle fonctionne, et la
    /// « normaliser » changerait sa valeur (ex. <c>« ␣1985-03-15 »</c>, <c>« 1985-3-5 »</c>).
    /// </summary>
    ReadableLeftAsIs,

    /// <summary>
    /// <b>Illisible</b> par le modèle courant (lève <see cref="FormatException"/>), <b>exactement</b> de la
    /// forme historique produite par <c>DateTime</c> avant P4-5D (<see cref="CivilDateFormat.IsLegacyDateTimeForm"/>,
    /// décisions D-B1 et Q-C2), et ses dix premiers caractères forment une date <c>yyyy-MM-dd</c> valide. La reprise est
    /// une <b>troncature</b> — la perte de la partie horaire est volontaire (ADR-PROD-DB-004 §2.4).
    /// </summary>
    RepairableLegacyDateTime,

    /// <summary>
    /// <b>Illisible</b> et non réparable par troncature : date inexistante, suffixe qui n'est pas une forme
    /// <c>DateTime</c> connue (<c>« 1985-03-15garbage »</c>, <c>« 1985-03-15 12:30 »</c> — D-B1), ou heure hors
    /// bornes (<c>« 1985-03-15 24:00:00 »</c>, <c>« … 12:60:00 »</c> — Q-C2). <b>Jamais transformée</b> : la
    /// corriger reviendrait à inventer une donnée. Elle est signalée pour intervention humaine.
    /// </summary>
    Unrepairable
}

/// <summary>
/// P4-5D-R — <b>règle de reprise des dates civiles</b>, isolée du stockage pour être prouvable seule.
///
/// <para>
/// <b>Le problème.</b> P4-5D a fait passer <c>Customer.BirthDate</c> et <c>Prescription.IssueDate</c> de
/// <c>DateTime</c> à <see cref="DateOnly"/>. Le type de colonne SQLite ne change pas (<c>TEXT</c>), donc
/// EF ne génère <b>aucune</b> migration — mais le <b>format</b> attendu, lui, change :
/// <c>« yyyy-MM-dd HH:mm:ss[.fffffff] »</c> devient <c>« yyyy-MM-dd »</c>. Une base antérieure à P4-5D
/// lève <see cref="FormatException"/> à la lecture (ADR-PROD-DB-004 §7.2).
/// </para>
///
/// <para>
/// <b>Deux règles non négociables</b>, mesurées et non supposées :
/// </para>
/// <list type="number">
/// <item>
/// <b>La lisibilité se teste avant tout le reste.</b> Certaines valeurs non canoniques sont <b>déjà lues
/// correctement</b> par le modèle (forme ISO <c>« 1985-03-15T00:00:00 »</c>, jour/mois non complétés
/// <c>« 1985-3-5 »</c>, espace de tête). Les traiter comme « à réparer » sans vérifier d'abord risquerait
/// de les <b>casser</b> : <c>« ␣1985-03-15 »</c> tronqué à dix caractères donne <c>« ␣1985-03-1 »</c>, qui
/// n'est plus lisible. On ne touche donc jamais une valeur avant d'avoir établi ce que le modèle en fait
/// aujourd'hui.
/// </item>
/// <item>
/// <b>La transformation est une troncature, jamais une conversion.</b> Aucun fuseau n'intervient : une date
/// de naissance ne change pas de jour. <c>« 1985-03-15 23:59:59 »</c> donne <c>« 1985-03-15 »</c>, jamais
/// le 16. La fonction SQLite <c>date()</c> est <b>proscrite</b> ici : elle « corrige » silencieusement
/// <c>« 2026-02-30 »</c> en <c>« 2026-03-02 »</c>, c'est-à-dire qu'elle <b>invente une date</b> au lieu de
/// signaler une donnée fausse.
/// </item>
/// <item>
/// <b>La troncature n'est accordée qu'à une forme connue</b> (P4-5D-R Phase C, décision D-B1). Une tête de
/// date valide ne suffit pas : <c>« 1985-03-15garbage »</c> n'a pas été écrit par <c>DateTime</c>, on ne
/// sait pas ce qu'il signifie, et le réduire à <c>« 1985-03-15 »</c> fabriquerait une date « propre » d'origine
/// inconnue. Il en va de même d'une heure impossible (<c>« 24:00:00 »</c>, Phase C2, décision Q-C2). Seule la
/// grammaire de <see cref="IsLegacyDateTimeForm"/> est reprise ; le reste est refusé.
/// </item>
/// </list>
///
/// <para>
/// <b>Fidélité au lecteur réel.</b> <see cref="IsReadableByCurrentModel"/> reproduit le comportement du
/// lecteur <c>Microsoft.Data.Sqlite</c> pour <see cref="DateOnly"/>. L'équivalence des deux — verdict
/// <i>et</i> valeur produite — est vérifiée par test sur l'ensemble des formes recensées : un détecteur qui
/// divergerait du lecteur serait pire qu'absent.
/// </para>
/// </summary>
public static class CivilDateFormat
{
    /// <summary>Forme cible : dix caractères, <c>yyyy-MM-dd</c>.</summary>
    public const string CanonicalFormat = "yyyy-MM-dd";

    /// <summary>Longueur de la forme canonique.</summary>
    public const int CanonicalLength = 10;

    /// <summary>
    /// Vrai si la valeur est lue sans erreur par le modèle courant (<see cref="DateOnly"/>). Reproduit le
    /// lecteur <c>Microsoft.Data.Sqlite</c> ; <paramref name="value"/> reçoit la date effectivement lue.
    /// </summary>
    public static bool IsReadableByCurrentModel(string? raw, out DateOnly value)
    {
        value = default;
        return raw is not null
               && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>
    /// Vrai si la valeur est déjà sous la forme cible exacte <c>yyyy-MM-dd</c>.
    /// </summary>
    public static bool IsCanonical(string? raw)
        => raw is not null
           && DateOnly.TryParseExact(raw, CanonicalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// Décision D-B1 — grammaire exacte des valeurs <c>DateTime</c> historiques :
    /// <c>yyyy-MM-dd</c>, <c>« ␣ »</c> ou <c>« T »</c>, <c>HH:mm:ss</c>, fraction facultative de 1 à 7 chiffres
    /// (le pilote omet les zéros de fin), suffixe ISO facultatif <c>Z</c> ou <c>±HH:mm</c>.
    /// <para>
    /// <b>Bornes horaires</b> (P4-5D-R Phase C2, décision Q-C2) : <c>00 ≤ HH ≤ 23</c>, <c>00 ≤ mm ≤ 59</c>,
    /// <c>00 ≤ ss ≤ 59</c>. <c>DateTime</c> n'écrit jamais <c>24:00:00</c> ni une seconde <c>60</c> : une telle
    /// valeur n'a pas été produite par l'application, et ISO 8601 lit <c>24:00:00</c> comme minuit <b>du
    /// lendemain</b>. Le suffixe de fuseau reste contrôlé sur sa <b>structure</b> seule : il n'est jamais
    /// appliqué, sa valeur est donc sans effet sur le résultat.
    /// </para>
    /// <para>
    /// <c>[0-9]</c> et non <c>\d</c>, qui accepterait des chiffres non ASCII ; <c>\z</c> et non <c>$</c>, qui
    /// accepterait un saut de ligne final. La validité calendaire de la tête reste vérifiée par
    /// <see cref="TryTruncateToCivilDate"/>. Le prédicat SQL support porte les mêmes bornes (addendum Phase B,
    /// prédicat <c>L'</c>).
    /// </para>
    /// </summary>
    private static readonly Regex LegacyDateTimeGrammar = new(
        @"^[0-9]{4}-[0-9]{2}-[0-9]{2}[ T]([01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})?\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>
    /// Vrai si la valeur a <b>exactement</b> la forme d'un <c>DateTime</c> écrit avant P4-5D (décisions D-B1 et
    /// Q-C2) : <c>yyyy-MM-dd[ T]HH:mm:ss[.f{1,7}][Z|±HH:mm]</c>, heure de <c>00:00:00</c> à <c>23:59:59</c>.
    /// Seule une telle valeur, illisible aujourd'hui, peut être reprise par troncature. Le suffixe de fuseau
    /// n'est <b>jamais appliqué</b> : il disparaît avec l'heure, et le jour retenu est le jour <b>écrit</b>.
    /// </summary>
    public static bool IsLegacyDateTimeForm(string? raw)
        => raw is not null && LegacyDateTimeGrammar.IsMatch(raw);

    /// <summary>
    /// Applique la transformation de reprise — <b>troncature aux dix premiers caractères</b> — et n'accepte
    /// le résultat que s'il forme une date <c>yyyy-MM-dd</c> valide. Aucune conversion de fuseau, aucun
    /// recalage de calendrier : <c>« 2026-02-30 »</c> est <b>refusé</b>, pas « corrigé ».
    /// <para>
    /// Primitive seule : elle ne juge pas ce qui suit la tête. <see cref="Classify"/> ne l'accorde qu'à une
    /// valeur déjà lue à l'identique par le modèle, ou de forme <see cref="IsLegacyDateTimeForm"/>.
    /// </para>
    /// </summary>
    public static bool TryTruncateToCivilDate(string? raw, out string canonical, out DateOnly value)
    {
        canonical = string.Empty;
        value = default;

        if (raw is null || raw.Length < CanonicalLength)
        {
            return false;
        }

        var head = raw.Substring(0, CanonicalLength);
        if (!DateOnly.TryParseExact(head, CanonicalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
        {
            return false;
        }

        canonical = head;
        return true;
    }

    /// <summary>
    /// Classe une valeur stockée. <paramref name="repairedValue"/> n'est renseigné que pour les états
    /// <see cref="CivilDateFormatState.NormalizableReadable"/> et
    /// <see cref="CivilDateFormatState.RepairableLegacyDateTime"/> — les seuls qui autorisent une écriture.
    ///
    /// <para>
    /// L'ordre des tests <b>est</b> la garantie de sûreté : on établit d'abord ce que le modèle lit
    /// aujourd'hui, et une valeur déjà lisible n'est réécrite que si la troncature redonne <b>exactement la
    /// même date</b>. Sinon elle est laissée intacte — mieux vaut une valeur non canonique qui fonctionne
    /// qu'une valeur « normalisée » qui a changé de sens.
    /// </para>
    /// </summary>
    public static CivilDateFormatState Classify(string? raw, out string repairedValue)
    {
        repairedValue = string.Empty;

        if (raw is null)
        {
            return CivilDateFormatState.NotApplicable;
        }

        if (IsCanonical(raw))
        {
            return CivilDateFormatState.Canonical;
        }

        if (IsReadableByCurrentModel(raw, out var readValue))
        {
            // Lisible mais non canonique. La réécriture n'est permise que si elle est DÉMONTRABLEMENT
            // neutre : même date avant et après. C'est ce qui protège « ␣1985-03-15 » et « 1985-3-5 ».
            if (TryTruncateToCivilDate(raw, out var canonical, out var truncated) && truncated == readValue)
            {
                repairedValue = canonical;
                return CivilDateFormatState.NormalizableReadable;
            }

            return CivilDateFormatState.ReadableLeftAsIs;
        }

        // Illisible : c'est le cas nominal d'une base antérieure à P4-5D — mais seulement si la valeur a la forme
        // exacte d'un DateTime historique, heure comprise entre 00:00:00 et 23:59:59 (D-B1, Q-C2). Une tête valide
        // suivie d'un suffixe inconnu ou d'une heure impossible est refusée : ce qui n'a pas été écrit par DateTime
        // n'a pas de sens établi, et la troncature en fabriquerait un.
        if (IsLegacyDateTimeForm(raw) && TryTruncateToCivilDate(raw, out var repaired, out _))
        {
            repairedValue = repaired;
            return CivilDateFormatState.RepairableLegacyDateTime;
        }

        return CivilDateFormatState.Unrepairable;
    }

    /// <summary>
    /// Vrai si l'état autorise une réécriture de la valeur stockée. Les deux seuls états concernés sont
    /// <see cref="CivilDateFormatState.NormalizableReadable"/> (mise en forme neutre) et
    /// <see cref="CivilDateFormatState.RepairableLegacyDateTime"/> (troncature volontaire).
    /// </summary>
    public static bool AllowsRewrite(CivilDateFormatState state)
        => state is CivilDateFormatState.NormalizableReadable or CivilDateFormatState.RepairableLegacyDateTime;
}
