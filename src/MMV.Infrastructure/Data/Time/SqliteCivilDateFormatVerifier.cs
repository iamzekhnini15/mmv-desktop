using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace MMV.Infrastructure.Data.Time;

/// <summary>
/// Une valeur de date civile qu'aucune règle sûre ne sait reprendre.
///
/// <para>
/// <b>Décision D-B3 — aucune date en clair au journal.</b> Une date de naissance est une donnée personnelle ;
/// une date d'ordonnance, une donnée de santé indirecte. La valeur brute reste disponible <b>en mémoire</b>
/// (<see cref="RawValue"/>) pour le diagnostic par code, mais <see cref="ToString"/> — la seule forme destinée
/// au journal de migration et aux messages d'erreur — ne la rend <b>jamais</b> : il donne table, colonne, clé
/// primaire et empreinte. La clé désigne la ligne ; l'empreinte permet de confirmer que la valeur lue en base
/// est bien celle qui a été refusée, et de regrouper des valeurs identiques.
/// </para>
///
/// <para>
/// <b>Protection minimale, pas anonymisation.</b> Une date a peu de valeurs possibles : une empreinte non salée
/// se retrouve par dictionnaire. Elle protège contre la lecture directe du journal (fichier partagé, capture
/// d'écran), pas contre un attaquant déterminé. La politique complète (rétention, clé secrète) relève de P7.
/// </para>
/// </summary>
public sealed class CivilDateOffendingValue
{
    /// <summary>Table concernée (<c>Customers</c> / <c>Prescriptions</c>).</summary>
    public required string Table { get; init; }

    /// <summary>Colonne concernée (<c>BirthDate</c> / <c>IssueDate</c>).</summary>
    public required string Column { get; init; }

    /// <summary>Nom de la colonne de clé primaire (<c>CustomerId</c> / <c>PrescriptionId</c>).</summary>
    public required string KeyColumn { get; init; }

    /// <summary>Clé primaire de la ligne, pour que le support puisse la retrouver et l'arbitrer.</summary>
    public required long RowId { get; init; }

    /// <summary>
    /// Valeur brute telle qu'elle est stockée. <b>Donnée personnelle : ne jamais la journaliser</b> (D-B3) —
    /// utiliser <see cref="ToString"/> ou <see cref="RawValueHash"/>.
    /// </summary>
    public required string RawValue { get; init; }

    /// <summary>
    /// Empreinte journalisable de <see cref="RawValue"/> : <c>sha256:</c> suivi des 64 chiffres hexadécimaux
    /// minuscules du SHA-256 de son encodage UTF-8 — reproductible par le support avec tout outil standard.
    /// </summary>
    public string RawValueHash
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RawValue))).ToLowerInvariant();

    /// <summary>
    /// Forme journalisable, <b>sans la valeur brute</b> (D-B3) :
    /// <c>Customers.BirthDate CustomerId=123 BirthDateHash=sha256:…</c>.
    /// </summary>
    public override string ToString() => $"{Table}.{Column} {KeyColumn}={RowId} {Column}Hash={RawValueHash}";
}

/// <summary>Bilan d'une colonne de date civile.</summary>
public sealed class CivilDateColumnAudit
{
    public required string Table { get; init; }
    public required string Column { get; init; }

    /// <summary>Vrai si la table et la colonne existent physiquement (sinon tous les compteurs sont nuls).</summary>
    public bool Exists { get; init; }

    /// <summary>Lignes examinées.</summary>
    public int TotalRows { get; init; }

    /// <summary>Valeurs <c>NULL</c> — hors périmètre de la reprise.</summary>
    public int NullCount { get; init; }

    /// <summary>Valeurs déjà au format cible <c>yyyy-MM-dd</c>.</summary>
    public int CanonicalCount { get; init; }

    /// <summary>Valeurs lisibles, non canoniques, dont la mise en forme est neutre.</summary>
    public int NormalizableCount { get; init; }

    /// <summary>Valeurs lisibles laissées intactes (les normaliser changerait leur valeur).</summary>
    public int ReadableLeftAsIsCount { get; init; }

    /// <summary>Valeurs illisibles réparables par troncature — le format historique d'avant P4-5D.</summary>
    public int RepairableCount { get; init; }

    /// <summary>Valeurs illisibles et non réparables : arbitrage humain requis.</summary>
    public int UnrepairableCount { get; init; }

    /// <summary>Les valeurs non réparables, nommément.</summary>
    public IReadOnlyList<CivilDateOffendingValue> UnrepairableValues { get; init; } = Array.Empty<CivilDateOffendingValue>();

    /// <summary>Nombre de lignes que la reprise réécrirait.</summary>
    public int RewriteCount => NormalizableCount + RepairableCount;
}

/// <summary>Bilan complet d'une base vis-à-vis du format des dates civiles.</summary>
public sealed class CivilDateFormatAudit
{
    public required IReadOnlyList<CivilDateColumnAudit> Columns { get; init; }

    /// <summary>Lignes que la reprise réécrirait, toutes colonnes confondues.</summary>
    public int TotalRewriteCount => Columns.Sum(c => c.RewriteCount);

    /// <summary>Lignes illisibles aujourd'hui (réparables ou non) : la base est cassée pour celles-ci.</summary>
    public int TotalUnreadableCount => Columns.Sum(c => c.RepairableCount + c.UnrepairableCount);

    /// <summary>Lignes non réparables, toutes colonnes confondues.</summary>
    public int TotalUnrepairableCount => Columns.Sum(c => c.UnrepairableCount);

    /// <summary>Toutes les valeurs non réparables, nommément.</summary>
    public IReadOnlyList<CivilDateOffendingValue> UnrepairableValues
        => Columns.SelectMany(c => c.UnrepairableValues).ToList();

    /// <summary>
    /// <b>Le signal de détection.</b> Vrai si la base porte au moins une valeur à réécrire : c'est une base
    /// antérieure à P4-5D (ou une base mixte). Faux sur une base saine — la reprise est alors sans objet.
    /// </summary>
    public bool RequiresRepair => TotalRewriteCount > 0;

    /// <summary>Vrai si au moins une valeur ne peut être reprise par aucune règle sûre.</summary>
    public bool HasUnrepairableValues => TotalUnrepairableCount > 0;
}

/// <summary>
/// P4-5D-R — <b>détection</b> du format des dates civiles sur une base SQLite. <b>Lecture seule</b> : ne
/// modifie jamais rien.
///
/// <para>
/// <b>Pourquoi lire en SQL brut.</b> Le modèle EF attend désormais <see cref="DateOnly"/> ; interroger
/// <c>Customers</c> par EF sur une base ancienne <b>lève</b> avant d'avoir pu constater quoi que ce soit.
/// Le diagnostic doit donc passer sous le modèle, en ADO, exactement comme
/// <see cref="SqliteDateTimeDefaultVerifier"/> le fait pour les <c>DEFAULT</c> physiques.
/// </para>
///
/// <para>
/// <b>Jamais de fausse alerte.</b> Une table ou une colonne absente est ignorée (base partielle), et la
/// classification de chaque valeur est déléguée à <see cref="CivilDateFormat"/>, dont l'accord avec le
/// lecteur réel est prouvé par test.
/// </para>
/// </summary>
public sealed class SqliteCivilDateFormatVerifier
{
    /// <summary>
    /// Les deux seules colonnes de dates civiles du modèle (ADR-PROD-DB-004 décision 7), avec leur clé
    /// primaire — les <b>19 colonnes d'instants ne sont pas concernées</b> : leur format stocké est
    /// inchangé, seul leur <c>Kind</c> relu l'est.
    /// </summary>
    public static readonly IReadOnlyList<(string Table, string Column, string KeyColumn)> CivilDateColumns = new[]
    {
        ("Customers", "BirthDate", "CustomerId"),
        ("Prescriptions", "IssueDate", "PrescriptionId"),
    };

    /// <summary>
    /// Audite la base associée au contexte. Ne modifie rien, ne lève pas pour une base partielle.
    /// </summary>
    public CivilDateFormatAudit Audit(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

            var existingTables = new HashSet<string>(ReadTableNames(connection), StringComparer.OrdinalIgnoreCase);
            var columns = new List<CivilDateColumnAudit>();

            foreach (var (table, column, keyColumn) in CivilDateColumns)
            {
                columns.Add(existingTables.Contains(table) && ColumnExists(connection, table, column)
                    ? AuditColumn(connection, table, column, keyColumn)
                    : new CivilDateColumnAudit { Table = table, Column = column, Exists = false });
            }

            return new CivilDateFormatAudit { Columns = columns };
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static CivilDateColumnAudit AuditColumn(
        DbConnection connection, string table, string column, string keyColumn)
    {
        var total = 0;
        var nulls = 0;
        var canonical = 0;
        var normalizable = 0;
        var readableLeftAsIs = 0;
        var repairable = 0;
        var unrepairable = new List<CivilDateOffendingValue>();

        using var command = connection.CreateCommand();
        // La valeur est lue en TEXT brut (CAST) : aucune conversion de type, donc aucune interprétation.
        command.CommandText = $"SELECT \"{keyColumn}\", CAST(\"{column}\" AS TEXT) FROM \"{table}\"";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            total++;
            if (reader.IsDBNull(1))
            {
                nulls++;
                continue;
            }

            var raw = reader.GetString(1);
            switch (CivilDateFormat.Classify(raw, out _))
            {
                case CivilDateFormatState.Canonical:
                    canonical++;
                    break;
                case CivilDateFormatState.NormalizableReadable:
                    normalizable++;
                    break;
                case CivilDateFormatState.ReadableLeftAsIs:
                    readableLeftAsIs++;
                    break;
                case CivilDateFormatState.RepairableLegacyDateTime:
                    repairable++;
                    break;
                default:
                    unrepairable.Add(new CivilDateOffendingValue
                    {
                        Table = table,
                        Column = column,
                        KeyColumn = keyColumn,
                        RowId = reader.GetInt64(0),
                        RawValue = raw
                    });
                    break;
            }
        }

        return new CivilDateColumnAudit
        {
            Table = table,
            Column = column,
            Exists = true,
            TotalRows = total,
            NullCount = nulls,
            CanonicalCount = canonical,
            NormalizableCount = normalizable,
            ReadableLeftAsIsCount = readableLeftAsIs,
            RepairableCount = repairable,
            UnrepairableCount = unrepairable.Count,
            UnrepairableValues = unrepairable
        };
    }

    private static bool ColumnExists(DbConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Colonne 1 de PRAGMA table_info = nom de la colonne.
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ReadTableNames(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
