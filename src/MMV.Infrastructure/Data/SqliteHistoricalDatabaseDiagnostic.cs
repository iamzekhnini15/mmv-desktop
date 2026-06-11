using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Décision recommandée par le diagnostic pour une base SQLite donnée (P2A-1B).
/// Ne contient que des orientations techniques ; aucune action destructive n'est impliquée.
/// </summary>
public enum HistoricalDatabaseDecision
{
    /// <summary>Aucun fichier de base (ou fichier SQLite valide mais vide, sans schéma applicatif).</summary>
    NoDatabaseFound,

    /// <summary>Le fichier existe mais n'est pas une base SQLite lisible (corrompu / autre format).</summary>
    RejectInvalidDatabase,

    /// <summary>Base déjà gérée par migrations (<c>__EFMigrationsHistory</c> présent), utilisable telle quelle.</summary>
    UseAsCurrentDatabase,

    /// <summary>Base historique (<c>EnsureCreated</c>) compatible avec le modèle courant : adoptable par baseline.</summary>
    AdoptCompatibleHistoricalDatabase,

    /// <summary>Ancien fichier valide et compatible, à recopier vers le chemin courant (décidé par l'orchestration de reprise).</summary>
    CopyLegacyDatabaseToCurrentPath,

    /// <summary>Schéma ancien / partiellement compatible / incompatible : reprise automatique refusée, intervention support requise.</summary>
    ManualRepairRequired
}

/// <summary>
/// Résultat structuré du diagnostic d'une base SQLite (P2A-1B). Ne contient que des métadonnées
/// techniques (chemins, tailles, dates, noms de tables, comptes de lignes, identifiants de
/// migration, divergences de schéma). <b>Aucune donnée personnelle</b> n'y figure.
/// </summary>
public sealed class HistoricalDatabaseDiagnosticResult
{
    /// <summary>Chemin du fichier de base diagnostiqué.</summary>
    public string DatabasePath { get; init; } = string.Empty;

    /// <summary>Vrai si le fichier existe sur le disque.</summary>
    public bool FileExists { get; init; }

    /// <summary>Taille du fichier en octets (0 si absent).</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Date de dernière modification du fichier (UTC), si disponible.</summary>
    public DateTime? LastModifiedUtc { get; init; }

    /// <summary>Vrai si le fichier est une base SQLite lisible.</summary>
    public bool IsValidSqlite { get; init; }

    /// <summary>Vrai si au moins une table applicative est présente.</summary>
    public bool HasApplicationTables { get; init; }

    /// <summary>Vrai si <c>__EFMigrationsHistory</c> est présent.</summary>
    public bool HasMigrationsHistory { get; init; }

    /// <summary>Migrations inscrites dans <c>__EFMigrationsHistory</c> (vide si absent).</summary>
    public IReadOnlyList<string> AppliedMigrations { get; init; } = Array.Empty<string>();

    /// <summary>Migrations définies par le modèle mais non inscrites dans la base.</summary>
    public IReadOnlyList<string> MissingMigrations { get; init; } = Array.Empty<string>();

    /// <summary>Vrai si le schéma réel est compatible avec le modèle EF courant.</summary>
    public bool IsSchemaCompatible { get; init; }

    /// <summary>Divergences de schéma détectées (vide si compatible).</summary>
    public IReadOnlyList<string> SchemaDifferences { get; init; } = Array.Empty<string>();

    /// <summary>Nombre de lignes par table applicative principale (métadonnée, pas de contenu).</summary>
    public IReadOnlyDictionary<string, long> TableRowCounts { get; init; }
        = new Dictionary<string, long>();

    /// <summary>Décision technique recommandée.</summary>
    public HistoricalDatabaseDecision RecommendedDecision { get; init; }

    /// <summary>Notes techniques additionnelles (sans donnée personnelle).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Diagnostic détaillé d'une base SQLite historique (P2A-1B). Inspecte une base existante
/// (validité, schéma, historique de migration, comptes de lignes) et recommande une décision de
/// reprise. <b>Lecture seule</b> : ne modifie jamais la base diagnostiquée. Ne journalise et
/// n'expose que des métadonnées techniques (jamais de données personnelles).
///
/// S'appuie sur le portail de compatibilité <see cref="SqliteSchemaVerifier"/> (P2A-1A-R2) pour
/// la comparaison de schéma, sans dupliquer la logique d'adoption de <see cref="SqliteDatabaseManager"/>.
/// </summary>
public sealed class SqliteHistoricalDatabaseDiagnostic
{
    /// <summary>Tables applicatives principales dont on rapporte le nombre de lignes.</summary>
    private static readonly string[] MainTables =
        { "Users", "Customers", "Products", "Prescriptions", "Orders", "Sales" };

    private readonly SqliteSchemaVerifier _schemaVerifier;

    public SqliteHistoricalDatabaseDiagnostic(SqliteSchemaVerifier? schemaVerifier = null)
    {
        _schemaVerifier = schemaVerifier ?? new SqliteSchemaVerifier();
    }

    /// <summary>
    /// Diagnostique la base associée au contexte fourni (sans la modifier). Le contexte doit pointer
    /// sur le fichier à examiner. Ne lève pas d'exception pour une base absente ou invalide : ces cas
    /// sont reflétés dans le résultat (<see cref="HistoricalDatabaseDecision.NoDatabaseFound"/> /
    /// <see cref="HistoricalDatabaseDecision.RejectInvalidDatabase"/>).
    /// </summary>
    public HistoricalDatabaseDiagnosticResult Diagnose(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var databasePath = GetDatabaseFilePath(context);
        var isFileDatabase = IsFileDatabase(databasePath);
        var notes = new List<string>();

        // 1) Existence / métadonnées du fichier.
        var fileExists = isFileDatabase && File.Exists(databasePath);
        long sizeBytes = 0;
        DateTime? lastModifiedUtc = null;
        if (fileExists)
        {
            var info = new FileInfo(databasePath);
            sizeBytes = info.Length;
            lastModifiedUtc = info.LastWriteTimeUtc;
        }

        if (isFileDatabase && !fileExists)
        {
            notes.Add("Fichier de base introuvable au chemin indiqué.");
            return new HistoricalDatabaseDiagnosticResult
            {
                DatabasePath = databasePath,
                FileExists = false,
                RecommendedDecision = HistoricalDatabaseDecision.NoDatabaseFound,
                Notes = notes
            };
        }

        // 2) Validité SQLite + lecture du schéma réel.
        List<string> tables;
        try
        {
            tables = ReadTableNames(context);
        }
        catch (SqliteException ex)
        {
            notes.Add($"Lecture du schéma impossible (SQLite {ex.SqliteErrorCode}) : fichier illisible ou non-SQLite.");
            return new HistoricalDatabaseDiagnosticResult
            {
                DatabasePath = databasePath,
                FileExists = fileExists,
                FileSizeBytes = sizeBytes,
                LastModifiedUtc = lastModifiedUtc,
                IsValidSqlite = false,
                RecommendedDecision = HistoricalDatabaseDecision.RejectInvalidDatabase,
                Notes = notes
            };
        }

        var hasHistory = tables.Contains(HistoryRepository.DefaultTableName, StringComparer.OrdinalIgnoreCase);
        var hasApplicationSchema = tables.Any(t =>
            !t.StartsWith("__", StringComparison.Ordinal) &&
            !t.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase));

        // 3) Base SQLite valide mais vide : rien à reprendre.
        if (!hasApplicationSchema && !hasHistory)
        {
            notes.Add("Base SQLite valide mais vide (aucune table applicative) : équivaut à une installation vierge.");
            return new HistoricalDatabaseDiagnosticResult
            {
                DatabasePath = databasePath,
                FileExists = fileExists,
                FileSizeBytes = sizeBytes,
                LastModifiedUtc = lastModifiedUtc,
                IsValidSqlite = true,
                RecommendedDecision = HistoricalDatabaseDecision.NoDatabaseFound,
                Notes = notes
            };
        }

        // 4) Comptes de lignes des tables principales présentes (métadonnée uniquement).
        var rowCounts = ReadMainTableRowCounts(context, tables);

        // 5) Historique des migrations.
        var allMigrations = SafeGetDefinedMigrations(context);
        var appliedMigrations = hasHistory ? ReadAppliedMigrations(context) : new List<string>();
        var missingMigrations = allMigrations
            .Where(m => !appliedMigrations.Contains(m, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // 6) Compatibilité de schéma (réutilise le portail P2A-1A-R2) — seulement si schéma applicatif présent.
        var schemaDifferences = new List<string>();
        var schemaCompatible = true;
        if (hasApplicationSchema)
        {
            var compatibility = _schemaVerifier.Verify(context);
            schemaCompatible = compatibility.IsCompatible;
            schemaDifferences = compatibility.Differences.ToList();
        }

        // 7) Décision recommandée.
        var decision = DecideForSingleDatabase(hasHistory, hasApplicationSchema, schemaCompatible, missingMigrations, notes);

        return new HistoricalDatabaseDiagnosticResult
        {
            DatabasePath = databasePath,
            FileExists = fileExists,
            FileSizeBytes = sizeBytes,
            LastModifiedUtc = lastModifiedUtc,
            IsValidSqlite = true,
            HasApplicationTables = hasApplicationSchema,
            HasMigrationsHistory = hasHistory,
            AppliedMigrations = appliedMigrations,
            MissingMigrations = missingMigrations,
            IsSchemaCompatible = schemaCompatible,
            SchemaDifferences = schemaDifferences,
            TableRowCounts = rowCounts,
            RecommendedDecision = decision,
            Notes = notes
        };
    }

    private static HistoricalDatabaseDecision DecideForSingleDatabase(
        bool hasHistory,
        bool hasApplicationSchema,
        bool schemaCompatible,
        IReadOnlyCollection<string> missingMigrations,
        List<string> notes)
    {
        if (hasHistory)
        {
            if (!schemaCompatible)
            {
                notes.Add("Base gérée par migrations mais schéma divergent du modèle courant : vérification support requise.");
                return HistoricalDatabaseDecision.ManualRepairRequired;
            }

            if (missingMigrations.Count > 0)
            {
                notes.Add($"Base gérée par migrations avec {missingMigrations.Count} migration(s) en attente : application normale au démarrage.");
            }

            return HistoricalDatabaseDecision.UseAsCurrentDatabase;
        }

        // Base historique (EnsureCreated) sans __EFMigrationsHistory.
        if (hasApplicationSchema && schemaCompatible)
        {
            return HistoricalDatabaseDecision.AdoptCompatibleHistoricalDatabase;
        }

        notes.Add("Schéma historique incompatible ou partiellement ancien : adoption automatique refusée (P2A-1B).");
        return HistoricalDatabaseDecision.ManualRepairRequired;
    }

    private static List<string> SafeGetDefinedMigrations(OpticDbContext context)
    {
        try
        {
            return context.Database.GetMigrations().ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static IReadOnlyDictionary<string, long> ReadMainTableRowCounts(OpticDbContext context, IReadOnlyCollection<string> tables)
    {
        var present = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

            foreach (var table in MainTables)
            {
                if (!present.Contains(table))
                {
                    continue;
                }

                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                    var scalar = command.ExecuteScalar();
                    counts[table] = scalar is null ? 0 : Convert.ToInt64(scalar);
                }
                catch (SqliteException)
                {
                    // Table présente mais illisible : non bloquant pour le diagnostic.
                }
            }

            return counts;
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static List<string> ReadAppliedMigrations(OpticDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT \"MigrationId\" FROM \"{HistoryRepository.DefaultTableName}\" ORDER BY \"MigrationId\"";
            using var reader = command.ExecuteReader();
            var ids = new List<string>();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }
        catch (SqliteException)
        {
            return new List<string>();
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static List<string> ReadTableNames(OpticDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

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
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static string GetDatabaseFilePath(OpticDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var builder = new SqliteConnectionStringBuilder(connection.ConnectionString);
        return builder.DataSource;
    }

    private static bool IsFileDatabase(string? dataSource)
        => !string.IsNullOrWhiteSpace(dataSource)
           && !string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
}
