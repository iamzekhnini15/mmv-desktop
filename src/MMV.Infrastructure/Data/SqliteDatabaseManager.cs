using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MMV.Infrastructure.Data;

/// <summary>
/// État détecté d'une base SQLite vis-à-vis des migrations EF Core.
/// </summary>
public enum DatabaseState
{
    /// <summary>Aucune table applicative ni historique de migration (installation vierge).</summary>
    Empty,

    /// <summary>
    /// Tables applicatives présentes mais <c>__EFMigrationsHistory</c> absent : base historique
    /// créée par <c>EnsureCreated()</c>, à adopter (baseline).
    /// </summary>
    HistoricalWithoutMigrationsHistory,

    /// <summary><c>__EFMigrationsHistory</c> présent : base déjà gérée par migrations.</summary>
    MigrationsManaged
}

/// <summary>
/// Résultat de la préparation d'une base (pour journal/tests).
/// </summary>
public sealed class DatabasePreparationResult
{
    public DatabaseState DetectedState { get; init; }
    public bool WasFreshInstall { get; init; }
    public bool WasAdopted { get; init; }
    public string? BackupPath { get; init; }
    public IReadOnlyList<string> AppliedMigrations { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BaselinedMigrations { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Cycle de vie professionnel et sûr de la base SQLite (P2A-1A) :
/// sauvegarde avant mutation, détection des bases historiques, adoption des migrations EF Core
/// (baseline), restauration, journal et échec explicite. Ne seede aucune donnée.
/// </summary>
public sealed class SqliteDatabaseManager
{
    /// <summary>Une table applicative connue, utilisée pour détecter un schéma historique.</summary>
    private const string KnownApplicationTable = "Users";

    private readonly IMigrationJournal _journal;

    public SqliteDatabaseManager(IMigrationJournal? journal = null)
    {
        _journal = journal ?? new MigrationJournal();
    }

    /// <summary>Journal des décisions/actions.</summary>
    public IMigrationJournal Journal => _journal;

    /// <summary>
    /// Prépare la base associée au contexte : chemin/dossier, sauvegarde, détection, migration ou
    /// adoption, vérification. Lève <see cref="DatabaseMigrationException"/> en cas d'échec, sans
    /// supprimer de données.
    /// </summary>
    public DatabasePreparationResult PrepareDatabase(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var databasePath = GetDatabaseFilePath(context);
        var isFileDatabase = IsFileDatabase(databasePath);

        if (isFileDatabase)
        {
            SqliteDatabasePathResolver.EnsureDirectoryExists(databasePath);
        }

        var fileExisted = isFileDatabase && File.Exists(databasePath);
        _journal.Write($"PREPARE start path='{databasePath}' fileExists={fileExisted}");

        // Sauvegarde AVANT toute mutation, dès qu'un fichier existe (même potentiellement invalide).
        string? backupPath = null;
        if (fileExisted)
        {
            backupPath = Backup(databasePath);
            _journal.Write($"BACKUP created '{backupPath}'");
        }

        var state = fileExisted || !isFileDatabase
            ? DetectState(context)
            : DatabaseState.Empty;
        _journal.Write($"DETECT state={state}");

        try
        {
            return state switch
            {
                DatabaseState.Empty => ApplyFreshInstall(context, backupPath),
                DatabaseState.MigrationsManaged => ApplyPendingMigrations(context, backupPath),
                DatabaseState.HistoricalWithoutMigrationsHistory => AdoptHistoricalDatabase(context, backupPath),
                _ => throw new DatabaseMigrationException($"État de base inattendu : {state}.")
            };
        }
        catch (DatabaseMigrationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _journal.Write($"FAILURE {ex.GetType().Name}: {ex.Message}");
            throw new DatabaseMigrationException(
                "Échec de la préparation de la base SQLite. La base et sa sauvegarde sont conservées " +
                "pour reprise manuelle (aucune donnée supprimée).", ex);
        }
    }

    /// <summary>
    /// Détecte l'état de la base. Lève <see cref="DatabaseMigrationException"/> si le fichier n'est
    /// pas une base SQLite valide (illisible/corrompu).
    /// </summary>
    public DatabaseState DetectState(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<string> tables;
        try
        {
            tables = GetTableNames(context);
        }
        catch (SqliteException ex)
        {
            throw new DatabaseMigrationException(
                "Le fichier de base SQLite est illisible ou corrompu : impossible de lire le schéma. " +
                "Aucune opération n'a été effectuée ; le fichier est conservé.", ex);
        }

        var hasHistory = tables.Contains(HistoryRepository.DefaultTableName, StringComparer.OrdinalIgnoreCase);
        var hasApplicationSchema = tables.Any(t =>
            !t.StartsWith("__", StringComparison.Ordinal) &&
            !t.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase));

        if (hasHistory)
        {
            return DatabaseState.MigrationsManaged;
        }

        return hasApplicationSchema
            ? DatabaseState.HistoricalWithoutMigrationsHistory
            : DatabaseState.Empty;
    }

    /// <summary>
    /// Sauvegarde le fichier SQLite (et ses fichiers annexes <c>-wal</c>/<c>-shm</c> s'ils existent)
    /// dans un sous-dossier <c>backups</c> horodaté. Retourne le chemin de la sauvegarde principale.
    /// </summary>
    public string Backup(string databasePath)
    {
        if (!IsFileDatabase(databasePath) || !File.Exists(databasePath))
        {
            throw new DatabaseMigrationException(
                $"Sauvegarde impossible : fichier de base introuvable '{databasePath}'.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".";
        var backupDirectory = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backupDirectory);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var fileName = Path.GetFileNameWithoutExtension(databasePath);
        var extension = Path.GetExtension(databasePath);
        var backupPath = Path.Combine(backupDirectory, $"{fileName}-{timestamp}{extension}.bak");

        File.Copy(databasePath, backupPath, overwrite: false);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
            {
                File.Copy(sidecar, backupPath + suffix, overwrite: false);
            }
        }

        return backupPath;
    }

    /// <summary>
    /// Restaure une base à partir d'une sauvegarde (fichier principal + annexes éventuels).
    /// Mécanisme de reprise « restauration du fichier SQLite sauvegardé » (ADR-003 §C).
    /// </summary>
    public void Restore(string backupPath, string databasePath)
    {
        if (!File.Exists(backupPath))
        {
            throw new DatabaseMigrationException($"Restauration impossible : sauvegarde introuvable '{backupPath}'.");
        }

        SqliteDatabasePathResolver.EnsureDirectoryExists(databasePath);
        File.Copy(backupPath, databasePath, overwrite: true);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecarBackup = backupPath + suffix;
            var sidecarTarget = databasePath + suffix;
            if (File.Exists(sidecarBackup))
            {
                File.Copy(sidecarBackup, sidecarTarget, overwrite: true);
            }
            else if (File.Exists(sidecarTarget))
            {
                // Le fichier annexe n'existait pas dans la sauvegarde : le retirer pour cohérence.
                File.Delete(sidecarTarget);
            }
        }

        _journal.Write($"RESTORE from '{backupPath}' to '{databasePath}'");
    }

    private DatabasePreparationResult ApplyFreshInstall(OpticDbContext context, string? backupPath)
    {
        context.Database.Migrate();
        var applied = context.Database.GetAppliedMigrations().ToList();
        VerifyAfterPreparation(context, requireHistory: true);
        _journal.Write($"FRESH install: applied {applied.Count} migration(s).");
        return new DatabasePreparationResult
        {
            DetectedState = DatabaseState.Empty,
            WasFreshInstall = true,
            BackupPath = backupPath,
            AppliedMigrations = applied
        };
    }

    private DatabasePreparationResult ApplyPendingMigrations(OpticDbContext context, string? backupPath)
    {
        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
        {
            context.Database.Migrate();
            _journal.Write($"MANAGED: applied {pending.Count} pending migration(s).");
        }
        else
        {
            _journal.Write("MANAGED: schema up to date (no pending migration).");
        }

        VerifyAfterPreparation(context, requireHistory: true);
        return new DatabasePreparationResult
        {
            DetectedState = DatabaseState.MigrationsManaged,
            BackupPath = backupPath,
            AppliedMigrations = pending
        };
    }

    /// <summary>
    /// Adopte une base historique (<c>EnsureCreated</c>, sans <c>__EFMigrationsHistory</c>).
    ///
    /// P2A-1A-R2 — <b>portail de compatibilité OBLIGATOIRE</b> : avant toute écriture de
    /// <c>__EFMigrationsHistory</c>, le schéma SQLite RÉEL est inspecté et comparé au modèle courant.
    /// Si une divergence significative est détectée, l'adoption est <b>refusée</b> (aucune migration
    /// inscrite), la base et sa sauvegarde sont conservées, et la raison est journalisée. Une base
    /// incompatible/ancienne relève de <b>P2A-1B</b>. Si le schéma est compatible, les migrations déjà
    /// reflétées sont inscrites (baseline) SANS ré-exécuter le DDL, puis les migrations réellement en
    /// attente sont appliquées. La donnée n'est jamais touchée par la baseline.
    /// </summary>
    private DatabasePreparationResult AdoptHistoricalDatabase(OpticDbContext context, string? backupPath)
    {
        var allMigrations = context.Database.GetMigrations().ToList();
        if (allMigrations.Count == 0)
        {
            throw new DatabaseMigrationException(
                "Adoption impossible : aucune migration définie dans l'assembly pour baseliner la base historique.");
        }

        // --- Portail de compatibilité de schéma (avant toute écriture d'historique) ---
        var compatibility = new SqliteSchemaVerifier().Verify(context);
        if (!compatibility.IsCompatible)
        {
            var reason = string.Join(" | ", compatibility.Differences);
            _journal.Write($"ADOPT REFUSED: incompatible historical schema ({compatibility.Differences.Count} divergence(s)): {reason}");
            throw new DatabaseMigrationException(
                "Base historique incompatible avec le modèle courant : adoption refusée. " +
                "Aucune migration n'a été inscrite dans __EFMigrationsHistory ; la base et sa sauvegarde " +
                "sont conservées (relève de P2A-1B). Divergences : " + reason + ".");
        }

        _journal.Write("ADOPT: schema compatibility gate passed.");

        // --- Baseline : inscription de l'historique (accès EF encapsulé) ---
        WriteMigrationHistory(context, allMigrations);
        _journal.Write($"ADOPT: baselined {allMigrations.Count} migration(s) into history.");

        // D'éventuelles migrations postérieures au schéma historique sont appliquées normalement.
        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
        {
            context.Database.Migrate();
            _journal.Write($"ADOPT: applied {pending.Count} pending migration(s) after baseline.");
        }

        VerifyAfterPreparation(context, requireHistory: true);
        return new DatabasePreparationResult
        {
            DetectedState = DatabaseState.HistoricalWithoutMigrationsHistory,
            WasAdopted = true,
            BackupPath = backupPath,
            BaselinedMigrations = allMigrations,
            AppliedMigrations = pending
        };
    }

    /// <summary>
    /// Point UNIQUE d'écriture de <c>__EFMigrationsHistory</c> (encapsule l'accès à l'API
    /// d'infrastructure EF Core <see cref="IHistoryRepository"/>, P2A-1A-R2 §6). Inscrit, dans une
    /// transaction, les identifiants de migration fournis, sans exécuter leur DDL.
    /// </summary>
    private static void WriteMigrationHistory(OpticDbContext context, IReadOnlyList<string> migrationIds)
    {
        var historyRepository = context.GetService<IHistoryRepository>();
        var productVersion = ProductInfo.GetVersion();

        using var transaction = context.Database.BeginTransaction();
        if (!historyRepository.Exists())
        {
            context.Database.ExecuteSqlRaw(historyRepository.GetCreateScript());
        }

        foreach (var migrationId in migrationIds)
        {
            var insertScript = historyRepository.GetInsertScript(new HistoryRow(migrationId, productVersion));
            context.Database.ExecuteSqlRaw(insertScript);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Vérifications post-préparation (P2A-1A-R2 §3) : aucune migration en attente, présence de
    /// <c>__EFMigrationsHistory</c> si requis, et capacité d'EF à lire les agrégats principaux.
    /// </summary>
    private void VerifyAfterPreparation(OpticDbContext context, bool requireHistory)
    {
        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
        {
            throw new DatabaseMigrationException(
                "La base reste incohérente après préparation : migrations toujours en attente : "
                + string.Join(", ", pending) + ".");
        }

        if (requireHistory && !GetTableNames(context).Contains(HistoryRepository.DefaultTableName, StringComparer.OrdinalIgnoreCase))
        {
            throw new DatabaseMigrationException(
                "Incohérence après adoption : __EFMigrationsHistory absent alors qu'il devrait être présent.");
        }

        try
        {
            // Confirme que le schéma préparé est réellement exploitable par EF (agrégats principaux).
            _ = context.Users.Any();
            _ = context.Customers.Any();
            _ = context.Products.Any();
        }
        catch (Exception ex)
        {
            throw new DatabaseMigrationException(
                "La base a été préparée mais reste illisible par EF (agrégats principaux inaccessibles).", ex);
        }
    }

    private static List<string> GetTableNames(OpticDbContext context)
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
