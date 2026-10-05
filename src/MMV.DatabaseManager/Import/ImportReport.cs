using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMV.DatabaseManager.Import;

/// <summary>Une table : lignes source, lignes de données initiales remplacées, lignes importées et vérifiées.</summary>
public sealed record ImportTableReport(string Table, long SourceRows, long SeedRowsReplaced, long ImportedRows, bool RowsVerified);

/// <summary>
/// Réconciliation d'une colonne <c>numeric</c> (ADR-003 §5.1) : total des valeurs source, total importé, écart, et
/// nombre de lignes dont l'arrondi a changé la valeur. Le total importé est <b>relu du serveur</b>.
/// </summary>
public sealed record ImportNumericReport(string Table, string Column, decimal SourceTotal, decimal ImportedTotal,
    decimal Difference, long RoundedRows, decimal? ServerTotal);

/// <summary>Dates civiles reprises par la règle P4-5D-R.</summary>
public sealed record ImportCivilDateReport(string Table, string Column, long ReadNonCanonical, long TruncatedLegacy);

/// <summary>Valeur notable ou refusée, localisée : table, clé, colonne.</summary>
public sealed record ImportFinding(string Table, string Key, string Column, string Detail);

/// <summary>
/// Rapport d'import (P4-7) : <b>résultat explicite</b> de chaque exécution, y compris refus et échecs. Ne contient
/// <b>aucun secret</b> (ni chaîne de connexion, ni mot de passe). Écrit dans un fichier <b>neuf</b> : jamais écrasé.
/// </summary>
public sealed class ImportReport
{
    /// <summary>Plafond des listes détaillées ; les compteurs restent exacts au-delà.</summary>
    public const int MaximumListedFindings = 1000;

    public Guid RunId { get; init; }
    public string ToolVersion { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public DateTime StartedAtUtc { get; init; }
    public DateTime? FinishedAtUtc { get; set; }

    public string Outcome { get; set; } = "open";
    public int ExitCode { get; set; } = -1;
    public string Message { get; set; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;
    public string? SourceSha256 { get; set; }
    public long? SourceBytes { get; set; }
    public IReadOnlyList<string> SourceMigrations { get; set; } = [];

    public string? TargetDatabase { get; set; }
    public long? TargetDatabaseOid { get; set; }
    public string? TargetSystemIdentifier { get; set; }
    public IReadOnlyList<string> TargetMigrations { get; set; } = [];
    public string? BackupId { get; set; }
    public string BackupReference { get; init; } = string.Empty;

    public string SourceTimeZone { get; init; } = string.Empty;
    public IReadOnlyList<string> Rules { get; } =
    [
        "Identifiants conservés : chaque clé primaire est importée telle quelle ; séquences d'identité recalées après import.",
        "__EFMigrationsHistory jamais importé : l'historique PostgreSQL reste celui de la chaîne PostgreSQL (ADR-005 §5.8, O11).",
        "Montants : numeric(p,s), arrondi à s décimales MidpointRounding.ToEven ; valeurs REAL déjà altérées à la source — résultat NON présenté comme une restitution fidèle (ADR-003 §5.1).",
        "Instants : texte EF sans fuseau interprété comme heure locale du magasin d'origine (fuseau SourceTimeZone) puis converti en UTC ; heure inexistante refusée, heure ambiguë = décalage standard, consignée (ADR-004 décision 8) ; tronqués à la microseconde, résolution de timestamp with time zone (compte : SubMicrosecondTruncatedInstants).",
        "Dates civiles : règle P4-5D-R (CivilDateFormat) ; forme historique tronquée à la date, forme irréparable refusée.",
        "Données initiales HasData présentes des deux côtés : lignes de la cible remplacées par celles de la source (O11).",
        "Une seule transaction PostgreSQL ; tout refus ou erreur l'annule. Retour arrière après validation : restore-backup de la sauvegarde vérifiée citée (R-9), jamais en place."
    ];

    public List<string> ExcludedSourceTables { get; } = [];
    public List<ImportTableReport> Tables { get; } = [];
    public List<ImportNumericReport> NumericColumns { get; } = [];
    public List<ImportCivilDateReport> CivilDates { get; } = [];

    /// <summary>Séquences d'identité recalées sur le plus grand identifiant importé.</summary>
    public int IdentitySequencesResynchronized { get; set; }

    /// <summary>Instants dont les 100 ns au-delà de la microseconde ont été tronqués.</summary>
    public long SubMicrosecondTruncatedInstants { get; set; }

    public long AmbiguousInstantCount { get; set; }
    public List<ImportFinding> AmbiguousInstants { get; } = [];
    public long RejectedValueCount { get; set; }
    public List<ImportFinding> RejectedValues { get; } = [];

    /// <summary>Après un échec : état de la cible relu et identique à l'état d'avant import (<c>null</c> = non relu).</summary>
    public bool? TargetUnchangedVerified { get; set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public void AddAmbiguous(ImportFinding finding)
    {
        AmbiguousInstantCount++;
        if (AmbiguousInstants.Count < MaximumListedFindings)
        {
            AmbiguousInstants.Add(finding);
        }
    }

    public void AddRejected(ImportFinding finding)
    {
        RejectedValueCount++;
        if (RejectedValues.Count < MaximumListedFindings)
        {
            RejectedValues.Add(finding);
        }
    }

    /// <summary>Crée le fichier (<see cref="FileMode.CreateNew"/>) : un rapport existant n'est jamais écrasé.</summary>
    public static void Reserve(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
}
