using System.Globalization;
using MMV.Infrastructure.Data.Time;

namespace MMV.DatabaseManager.Import;

/// <summary>Ce qu'une conversion a dû faire, consigné au rapport — jamais en silence (ADR-003 §5.1, ADR-004 §8).</summary>
public enum ImportNote
{
    None,

    /// <summary>Montant arrondi à l'échelle de la colonne (<see cref="MidpointRounding.ToEven"/>) : valeur source changée.</summary>
    Rounded,

    /// <summary>Heure locale ambiguë (retour à l'heure d'hiver) : décalage standard du fuseau retenu.</summary>
    AmbiguousLocalTime,

    /// <summary>Instant tronqué à la microseconde (résolution de <c>timestamp with time zone</c>).</summary>
    SubMicrosecondTruncated,

    /// <summary>Date civile lisible non canonique, valeur lue par le modèle courant (P4-5D-R).</summary>
    CivilDateReadable,

    /// <summary>Date civile au format historique <c>DateTime</c>, tronquée à la date (P4-5D-R).</summary>
    CivilDateTruncated
}

/// <summary>Résultat d'une conversion : valeur cible, ou cause de refus.</summary>
public readonly record struct ImportConversion(object? Value, string? Error, ImportNote Note, decimal? SourceDecimal)
{
    public bool IsValid => Error is null;

    public static ImportConversion Refused(string error) => new(null, error, ImportNote.None, null);
}

/// <summary>
/// Conversion SQLite → PostgreSQL (P4-7), <b>stricte</b> : le type physique cible décide, la classe de stockage SQLite
/// doit être celle qu'EF écrit, et toute valeur hors règle est refusée avec sa cause. Règles :
/// <list type="bullet">
///   <item>montants : <c>numeric(p,s)</c>, arrondi à <c>s</c> décimales <see cref="MidpointRounding.ToEven"/>, comme
///   <c>Money</c> (ADR-003 §5.1) ; la valeur source est celle du <c>REAL</c> relu au plus court (« R ») ;</item>
///   <item>instants : texte EF sans fuseau, <b>interprété comme heure locale du magasin d'origine</b> puis converti en
///   UTC (ADR-004 décision 8) ; heure inexistante refusée, heure ambiguë consignée ;</item>
///   <item>dates civiles : règle P4-5D-R (<see cref="CivilDateFormat"/>) ; irréparable refusée ;</item>
///   <item>textes : aucun caractère NUL, longueur en caractères ≤ <c>character varying(n)</c>, énumération par
///   <b>nom exact</b>.</item>
/// </list>
/// </summary>
public static class ImportValues
{
    private static readonly string[] InstantFormats =
    [
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "yyyy-MM-ddTHH:mm:ss"
    ];

    public static ImportConversion Convert(ImportColumn column, object? raw, TimeZoneInfo sourceZone)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(sourceZone);

        if (raw is null or DBNull)
        {
            return column.IsNullable
                ? new ImportConversion(null, null, ImportNote.None, null)
                : ImportConversion.Refused("NULL dans une colonne NOT NULL");
        }

        return column.Kind switch
        {
            ImportColumnKind.SmallInt => Integer(raw, short.MinValue, short.MaxValue, v => (short)v),
            ImportColumnKind.Integer => Integer(raw, int.MinValue, int.MaxValue, v => (int)v),
            ImportColumnKind.BigInt => Integer(raw, long.MinValue, long.MaxValue, v => v),
            ImportColumnKind.Boolean => raw is long b && b is 0 or 1
                ? new ImportConversion((long)raw == 1, null, ImportNote.None, null)
                : ImportConversion.Refused($"booléen attendu (0 ou 1), lu {Describe(raw)}"),
            ImportColumnKind.Numeric => Numeric(column, raw),
            ImportColumnKind.Double => raw switch
            {
                double d => new ImportConversion(d, null, ImportNote.None, null),
                long l => new ImportConversion((double)l, null, ImportNote.None, null),
                _ => ImportConversion.Refused($"nombre attendu, lu {Describe(raw)}")
            },
            ImportColumnKind.Text => Text(column, raw),
            ImportColumnKind.Instant => Instant(raw, sourceZone),
            ImportColumnKind.CivilDate => CivilDate(raw),
            _ => throw new ArgumentOutOfRangeException(nameof(column))
        };
    }

    /// <summary>
    /// Forme textuelle canonique d'une valeur cible — celle produite par <see cref="Convert"/> comme celle relue du
    /// serveur — pour la comparaison ligne à ligne avant validation.
    /// </summary>
    public static string Canonical(ImportColumn column, object? value)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (value is null or DBNull)
        {
            return "∅";
        }

        return column.Kind switch
        {
            ImportColumnKind.SmallInt or ImportColumnKind.Integer or ImportColumnKind.BigInt =>
                System.Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            ImportColumnKind.Boolean => (bool)value ? "true" : "false",
            ImportColumnKind.Numeric => System.Convert.ToDecimal(value, CultureInfo.InvariantCulture)
                .ToString("F" + column.Scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            ImportColumnKind.Double => System.Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
            ImportColumnKind.Text => (string)value,
            ImportColumnKind.Instant => value switch
            {
                DateTime { Kind: DateTimeKind.Utc } utc => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                DateTimeOffset offset => offset.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                _ => throw new InvalidCastException($"Instant non UTC : {value} ({value.GetType().Name}).")
            },
            ImportColumnKind.CivilDate => value switch
            {
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime dateTime => DateOnly.FromDateTime(dateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => throw new InvalidCastException($"Date inattendue : {value.GetType().Name}.")
            },
            _ => throw new ArgumentOutOfRangeException(nameof(column))
        };
    }

    private static ImportConversion Integer(object raw, long min, long max, Func<long, object> narrow) =>
        raw is long value
            ? value >= min && value <= max
                ? new ImportConversion(narrow(value), null, ImportNote.None, null)
                : ImportConversion.Refused(string.Create(CultureInfo.InvariantCulture, $"entier {value} hors de [{min}, {max}]"))
            : ImportConversion.Refused($"entier attendu, lu {Describe(raw)}");

    private static ImportConversion Numeric(ImportColumn column, object raw)
    {
        decimal source;
        switch (raw)
        {
            case long l:
                source = l;
                break;
            case double d when double.IsFinite(d):
                // Valeur du REAL telle que relue au plus court : c'est elle que l'application affichait.
                if (!decimal.TryParse(d.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out source))
                {
                    return ImportConversion.Refused($"montant {Describe(raw)} hors de la plage décimale");
                }

                break;
            case string s when decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed):
                source = parsed;
                break;
            default:
                return ImportConversion.Refused($"montant attendu, lu {Describe(raw)}");
        }

        var rounded = Math.Round(source, column.Scale, MidpointRounding.ToEven);
        var limit = Pow10(column.Precision - column.Scale);
        if (Math.Abs(rounded) >= limit)
        {
            return ImportConversion.Refused(string.Create(CultureInfo.InvariantCulture,
                $"montant {source} hors de {column.StoreType}"));
        }

        return new ImportConversion(rounded, null, rounded == source ? ImportNote.None : ImportNote.Rounded, source);
    }

    private static ImportConversion Text(ImportColumn column, object raw)
    {
        if (raw is not string text)
        {
            return ImportConversion.Refused($"texte attendu, lu {Describe(raw)}");
        }

        if (text.Contains('\0'))
        {
            return ImportConversion.Refused("caractère NUL (refusé par PostgreSQL)");
        }

        if (column.MaxLength is { } max)
        {
            var length = text.EnumerateRunes().Count();
            if (length > max)
            {
                return ImportConversion.Refused(string.Create(CultureInfo.InvariantCulture,
                    $"{length} caractères pour {column.StoreType}"));
            }
        }

        if (column.EnumType is { } enumType && !Enum.GetNames(enumType).Contains(text, StringComparer.Ordinal))
        {
            return ImportConversion.Refused($"'{Shorten(text)}' n'est pas une valeur de {enumType.Name}");
        }

        return new ImportConversion(text, null, ImportNote.None, null);
    }

    private static ImportConversion Instant(object raw, TimeZoneInfo zone)
    {
        if (raw is not string text
            || !DateTime.TryParseExact(text, InstantFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return ImportConversion.Refused($"instant au format EF sans fuseau attendu, lu {Describe(raw)}");
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            return ImportConversion.Refused($"heure locale '{text}' inexistante dans le fuseau {zone.Id}");
        }

        // Décalage du fuseau à cette heure locale ; pour une heure ambiguë, le décalage standard. Plage vérifiée ici :
        // aucune valeur n'est ramenée en silence à DateTime.MinValue ou MaxValue.
        var ticks = local.Ticks - zone.GetUtcOffset(local).Ticks;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return ImportConversion.Refused($"instant '{text}' hors de la plage représentable en UTC");
        }

        var utc = new DateTime(ticks, DateTimeKind.Utc);
        var truncated = TruncateToMicroseconds(utc);
        var note = zone.IsAmbiguousTime(local) ? ImportNote.AmbiguousLocalTime
            : truncated != utc ? ImportNote.SubMicrosecondTruncated
            : ImportNote.None;
        return new ImportConversion(truncated, null, note, null);
    }

    private static ImportConversion CivilDate(object raw)
    {
        if (raw is not string text)
        {
            return ImportConversion.Refused($"date civile textuelle attendue, lu {Describe(raw)}");
        }

        var state = CivilDateFormat.Classify(text, out var repaired);
        switch (state)
        {
            case CivilDateFormatState.Canonical:
            case CivilDateFormatState.NormalizableReadable:
            case CivilDateFormatState.ReadableLeftAsIs:
                CivilDateFormat.IsReadableByCurrentModel(text, out var read);
                return new ImportConversion(read, null,
                    state == CivilDateFormatState.Canonical ? ImportNote.None : ImportNote.CivilDateReadable, null);
            case CivilDateFormatState.RepairableLegacyDateTime:
                return new ImportConversion(DateOnly.ParseExact(repaired, CivilDateFormat.CanonicalFormat, CultureInfo.InvariantCulture),
                    null, ImportNote.CivilDateTruncated, null);
            default:
                return ImportConversion.Refused($"date civile irréparable '{Shorten(text)}' (P4-5D-R)");
        }
    }

    /// <summary>
    /// <c>timestamp with time zone</c> est à la microseconde ; SQLite garde les 100 ns de <c>DateTime</c>. Npgsql
    /// tronquerait ; la troncature est donc faite ici, explicitement, et comptée au rapport.
    /// </summary>
    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, value.Kind);

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }

    private static string Describe(object raw) => raw switch
    {
        string s => $"texte '{Shorten(s)}'",
        long l => string.Create(CultureInfo.InvariantCulture, $"entier {l}"),
        double d => string.Create(CultureInfo.InvariantCulture, $"réel {d:R}"),
        byte[] b => string.Create(CultureInfo.InvariantCulture, $"blob de {b.Length} octet(s)"),
        _ => raw.GetType().Name
    };

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";
}
