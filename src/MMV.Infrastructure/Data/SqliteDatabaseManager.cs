using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Constants;

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

        // --- P3-8 : refus explicite d'un schéma de résolution des notifications PARTIEL ou ALTÉRÉ (avant toute
        //     écriture). AddNotificationResolution ajoute, dans une seule migration, la colonne Notifications.
        //     ResolvedAt ET l'index unique filtré protégeant l'unicité des alertes de stock bas actives. Un état où
        //     l'un existe sans l'autre — ou où un index porte le même nom sans le filtre attendu — ne peut être
        //     produit par une exécution normale de cette migration : c'est un schéma altéré à la main ou un
        //     historique mensonger. On refuse ici, AVANT toute inscription dans __EFMigrationsHistory, plutôt que de
        //     baseliner une protection multi-poste qui n'existe pas physiquement.
        var resolvedAtColumnPresent = ColumnExists(context, NotificationsTableName, ResolvedAtColumnName);
        var (activeLowStockIndexExists, activeLowStockIndexValid) = InspectActiveLowStockUniqueIndex(context);

        if (!resolvedAtColumnPresent && activeLowStockIndexExists)
        {
            _journal.Write(
                "ADOPT REFUSED: notification active low-stock index present without ResolvedAt column (homonym or leftover index).");
            throw new DatabaseMigrationException(
                "Schéma de résolution des notifications incohérent : un index nommé idx_notifications_active_low_stock_unique " +
                "existe alors que la colonne Notifications.ResolvedAt qu'il filtre est absente. Adoption refusée. " +
                "Aucune migration n'a été inscrite dans __EFMigrationsHistory ; la base et sa sauvegarde sont conservées.");
        }

        if (resolvedAtColumnPresent && !(activeLowStockIndexExists && activeLowStockIndexValid))
        {
            _journal.Write(
                $"ADOPT REFUSED: partial notification resolution schema (ResolvedAt present, active low-stock unique index " +
                $"present={activeLowStockIndexExists}, valid={activeLowStockIndexValid}).");
            throw new DatabaseMigrationException(
                "Schéma de résolution des notifications partiel : la colonne Notifications.ResolvedAt est présente " +
                "mais l'index unique filtré idx_notifications_active_low_stock_unique protégeant les alertes de stock " +
                "bas actives est absent ou mal défini. Adoption refusée. Aucune migration n'a été inscrite dans " +
                "__EFMigrationsHistory ; la base et sa sauvegarde sont conservées.");
        }

        // --- P3-6B : refus explicite d'une extension de fiche atelier PARTIELLE (avant toute écriture) ---
        // Les deux tables sont créées ensemble par AddWorkshopSheets : n'en trouver qu'une seule signale une base
        // altérée à la main ou une adoption précédemment interrompue. Les deux issues automatiques seraient
        // fausses — baseliner marquerait une table manquante comme créée, et exécuter la migration ferait échouer
        // un CreateTable sur la table déjà présente, avec un message technique brut. On échoue donc ici, AVANT
        // toute inscription dans __EFMigrationsHistory : aucune fausse entrée n'est écrite.
        var tablesBeforeAdoption = GetTableNames(context);
        var hasWorkshopSheets = tablesBeforeAdoption.Contains(WorkshopSheetsTableName, StringComparer.OrdinalIgnoreCase);
        var hasWorkshopSheetItems = tablesBeforeAdoption.Contains(WorkshopSheetItemsTableName, StringComparer.OrdinalIgnoreCase);

        if (hasWorkshopSheets != hasWorkshopSheetItems)
        {
            var present = hasWorkshopSheets ? WorkshopSheetsTableName : WorkshopSheetItemsTableName;
            var missing = hasWorkshopSheets ? WorkshopSheetItemsTableName : WorkshopSheetsTableName;
            _journal.Write($"ADOPT REFUSED: partial workshop sheet schema (present: {present}, missing: {missing}).");
            throw new DatabaseMigrationException(
                $"Schéma de fiche atelier partiel : la table {present} est présente alors que {missing} est " +
                "absente. Ces deux tables sont créées ensemble ; adoption refusée. Aucune migration n'a été " +
                "inscrite dans __EFMigrationsHistory ; la base et sa sauvegarde sont conservées.");
        }

        // --- P2A-1R19-R2 : détection des anciens DEFAULT DateTime figés (R-19) physiquement présents ---
        // Le portail de compatibilité ci-dessus ne compare PAS les valeurs DEFAULT. Une base historique
        // créée par EnsureCreated AVANT P2A-1R19 reste donc « compatible » tout en conservant les anciens
        // DEFAULT figés sur les 7 colonnes corrigées. Baseliner TOUTES les migrations (dont
        // FixDateTimeDefaultValues) marquerait à tort la correction comme appliquée alors que les DEFAULT
        // hérités subsistent en base (baseline mensonger). On limite alors la baseline aux migrations
        // réellement reflétées par le schéma physique et on EXÉCUTE réellement FixDateTimeDefaultValues
        // pour supprimer les DEFAULT hérités (réparation contrôlée, non destructive — preuve P2A-1R19).
        var legacyDefaultVerifier = new SqliteDateTimeDefaultVerifier();
        var legacyDefaultColumns = legacyDefaultVerifier.GetColumnsWithLegacyDefaults(context);

        // Certaines migrations doivent être EXÉCUTÉES (et non baselinées) parce que leur effet physique est
        // ABSENT du schéma historique. On baseline tout ce qui précède la PREMIÈRE de ces migrations, puis on
        // applique réellement le reste (la migration concernée et les suivantes).
        // (a) R-19 : FixDateTimeDefaultValues si les anciens DEFAULT DateTime figés sont physiquement présents.
        // (b) P2A-1E : AddDocumentSequences si la table DocumentSequences est physiquement absente (base
        //     antérieure à P2A-1E). Sans cela, baseliner cette migration marquerait à tort la table comme créée.
        var documentSequencesMissing = !GetTableNames(context)
            .Contains(DocumentSequencesTableName, StringComparer.OrdinalIgnoreCase);
        var firstIndexToExecute = allMigrations.Count;

        if (legacyDefaultColumns.Count > 0)
        {
            var fixIndex = allMigrations.FindIndex(IsFixDateTimeDefaultsMigration);
            if (fixIndex >= 0)
            {
                firstIndexToExecute = Math.Min(firstIndexToExecute, fixIndex);
                _journal.Write(
                    $"ADOPT: legacy DateTime DEFAULT detected on {legacyDefaultColumns.Count} column(s) " +
                    $"[{string.Join(", ", legacyDefaultColumns)}] — FixDateTimeDefaultValues will be executed (R-19 repair).");
            }
        }

        if (documentSequencesMissing)
        {
            var sequencesIndex = allMigrations.FindIndex(IsAddDocumentSequencesMigration);
            if (sequencesIndex >= 0)
            {
                firstIndexToExecute = Math.Min(firstIndexToExecute, sequencesIndex);
                _journal.Write(
                    "ADOPT: DocumentSequences table absent (base antérieure à P2A-1E) — " +
                    "AddDocumentSequences will be executed (numbering table created).");
            }
        }

        // (c) P3-2B : AddCustomerArchivingAndProtectHistory si la colonne Customers.IsArchived est physiquement
        //     absente (base antérieure à P3-2B). Baseliner cette migration marquerait à tort la colonne comme
        //     ajoutée ET les clés étrangères Prescriptions/Sales → Customers comme passées en Restrict, alors que
        //     le schéma historique porte encore Cascade/SetNull : l'historique métier resterait destructible.
        var customerArchivingMissing = !ColumnExists(context, CustomersTableName, IsArchivedColumnName);
        if (customerArchivingMissing)
        {
            var archivingIndex = allMigrations.FindIndex(IsAddCustomerArchivingMigration);
            if (archivingIndex >= 0)
            {
                firstIndexToExecute = Math.Min(firstIndexToExecute, archivingIndex);
                _journal.Write(
                    "ADOPT: Customers.IsArchived column absent (base antérieure à P3-2B) — " +
                    "AddCustomerArchivingAndProtectHistory will be executed (archiving column + Restrict FKs).");
            }
        }

        // (d) P3-6B : AddWorkshopSheets si la table WorkshopSheets est physiquement absente (base antérieure à
        //     P3-6B). Baseliner cette migration marquerait à tort les tables de fiche atelier comme créées : la
        //     génération automatique de la première fiche échouerait alors au premier passage en fabrication.
        //     L'état partiel ayant déjà été refusé plus haut, l'absence de WorkshopSheets vaut absence des deux.
        var workshopSheetsMissing = !hasWorkshopSheets;
        if (workshopSheetsMissing)
        {
            var workshopIndex = allMigrations.FindIndex(IsAddWorkshopSheetsMigration);
            if (workshopIndex >= 0)
            {
                firstIndexToExecute = Math.Min(firstIndexToExecute, workshopIndex);
                _journal.Write(
                    "ADOPT: WorkshopSheets table absent (base antérieure à P3-6B) — " +
                    "AddWorkshopSheets will be executed (workshop sheet tables created).");
            }
        }

        // (e) P3-8 : AddNotificationResolution si la colonne Notifications.ResolvedAt est physiquement absente (base
        //     antérieure à P3-8). Baseliner cette migration marquerait à tort la colonne ET l'index unique filtré
        //     comme créés, laissant les alertes de stock bas sans protection multi-poste ni distinction
        //     active/résolue (historique mensonger). Le cas partiel (colonne présente sans index valide, ou index
        //     homonyme sans colonne) a déjà été refusé plus haut.
        if (!resolvedAtColumnPresent)
        {
            var notificationResolutionIndex = allMigrations.FindIndex(IsAddNotificationResolutionMigration);
            if (notificationResolutionIndex >= 0)
            {
                firstIndexToExecute = Math.Min(firstIndexToExecute, notificationResolutionIndex);
                _journal.Write(
                    "ADOPT: Notifications.ResolvedAt column absent (base antérieure à P3-8) — " +
                    "AddNotificationResolution will be executed (resolution column + unique filtered index created, historical duplicates normalized).");
            }
        }

        var migrationsToBaseline = allMigrations.Take(firstIndexToExecute).ToList();

        // --- Baseline : inscription de l'historique (accès EF encapsulé) ---
        WriteMigrationHistory(context, migrationsToBaseline);
        _journal.Write($"ADOPT: baselined {migrationsToBaseline.Count} migration(s) into history.");

        // Migrations non encore reflétées par le schéma physique (dont FixDateTimeDefaultValues) :
        // appliquées réellement (et non baselinées).
        var pending = context.Database.GetPendingMigrations().ToList();
        if (pending.Count > 0)
        {
            context.Database.Migrate();
            _journal.Write($"ADOPT: applied {pending.Count} pending migration(s) after baseline.");
        }

        VerifyAfterPreparation(context, requireHistory: true);

        // --- P2A-1R19-R2 : vérification physique post-adoption — aucun ancien DEFAULT DateTime ne subsiste ---
        // Garantit que __EFMigrationsHistory reflète réellement le schéma physique : pas d'acceptation
        // silencieuse d'une base portant encore les DEFAULT hérités (échec explicite, base + sauvegarde conservées).
        var remainingLegacyDefaults = legacyDefaultVerifier.GetColumnsWithLegacyDefaults(context);
        if (remainingLegacyDefaults.Count > 0)
        {
            _journal.Write(
                $"ADOPT INCONSISTENT: legacy DateTime DEFAULT still present after adoption " +
                $"[{string.Join(", ", remainingLegacyDefaults)}].");
            throw new DatabaseMigrationException(
                "Incohérence après adoption : des valeurs par défaut DateTime héritées (R-19) subsistent " +
                "physiquement sur " + string.Join(", ", remainingLegacyDefaults) + ". __EFMigrationsHistory " +
                "ne reflète pas le schéma réel ; la base et sa sauvegarde sont conservées.");
        }

        // --- P2A-1E : vérification physique post-adoption — la table de numérotation existe réellement ---
        // Garantit qu'aucune base n'est marquée « AddDocumentSequences appliquée » sans la table physique
        // (pas de baseline mensonger sur la numérotation : échec explicite, base + sauvegarde conservées).
        if (!GetTableNames(context).Contains(DocumentSequencesTableName, StringComparer.OrdinalIgnoreCase))
        {
            _journal.Write("ADOPT INCONSISTENT: DocumentSequences table absent after adoption.");
            throw new DatabaseMigrationException(
                "Incohérence après adoption : la table de numérotation DocumentSequences est absente alors " +
                "que la migration AddDocumentSequences est inscrite. __EFMigrationsHistory ne reflète pas le " +
                "schéma réel ; la base et sa sauvegarde sont conservées.");
        }

        // --- P3-2B : vérification physique post-adoption — la colonne d'archivage existe réellement ---
        // Garantit qu'aucune base n'est marquée « AddCustomerArchivingAndProtectHistory appliquée » sans la
        // colonne physique (un baseline mensonger laisserait aussi les FK en Cascade/SetNull, donc l'historique
        // métier destructible).
        if (!ColumnExists(context, CustomersTableName, IsArchivedColumnName))
        {
            _journal.Write("ADOPT INCONSISTENT: Customers.IsArchived column absent after adoption.");
            throw new DatabaseMigrationException(
                "Incohérence après adoption : la colonne d'archivage Customers.IsArchived est absente alors " +
                "que la migration AddCustomerArchivingAndProtectHistory est inscrite. __EFMigrationsHistory ne " +
                "reflète pas le schéma réel ; la base et sa sauvegarde sont conservées.");
        }

        // --- P3-6B : vérification physique post-adoption — les tables de fiche atelier existent réellement ---
        // Garantit qu'aucune base n'est marquée « AddWorkshopSheets appliquée » sans les tables physiques (un
        // baseline mensonger ferait échouer la génération automatique de fiche au passage en fabrication).
        var tablesAfterAdoption = GetTableNames(context);
        if (!tablesAfterAdoption.Contains(WorkshopSheetsTableName, StringComparer.OrdinalIgnoreCase)
            || !tablesAfterAdoption.Contains(WorkshopSheetItemsTableName, StringComparer.OrdinalIgnoreCase))
        {
            _journal.Write("ADOPT INCONSISTENT: WorkshopSheets table(s) absent after adoption.");
            throw new DatabaseMigrationException(
                "Incohérence après adoption : les tables de fiche atelier (WorkshopSheets / WorkshopSheetItems) " +
                "sont absentes alors que la migration AddWorkshopSheets est inscrite. __EFMigrationsHistory ne " +
                "reflète pas le schéma réel ; la base et sa sauvegarde sont conservées.");
        }

        // Note : la cohérence physique de Notifications.ResolvedAt et de son index unique filtré (P3-8) est déjà
        // garantie inconditionnellement par VerifyAfterPreparation ci-dessus (appelée avant ce point) — inutile de
        // la revérifier ici.

        return new DatabasePreparationResult
        {
            DetectedState = DatabaseState.HistoricalWithoutMigrationsHistory,
            WasAdopted = true,
            BackupPath = backupPath,
            BaselinedMigrations = migrationsToBaseline,
            AppliedMigrations = pending
        };
    }

    /// <summary>Suffixe de l'identifiant de la migration technique R-19 (P2A-1R19).</summary>
    private const string FixDateTimeDefaultsMigrationSuffix = "_FixDateTimeDefaultValues";

    private static bool IsFixDateTimeDefaultsMigration(string migrationId)
        => migrationId.EndsWith(FixDateTimeDefaultsMigrationSuffix, StringComparison.Ordinal);

    /// <summary>Nom de la table de numérotation des documents (P2A-1E, R-03).</summary>
    private const string DocumentSequencesTableName = "DocumentSequences";

    /// <summary>Suffixe de l'identifiant de la migration additive P2A-1E (table DocumentSequences).</summary>
    private const string AddDocumentSequencesMigrationSuffix = "_AddDocumentSequences";

    private static bool IsAddDocumentSequencesMigration(string migrationId)
        => migrationId.EndsWith(AddDocumentSequencesMigrationSuffix, StringComparison.Ordinal);

    /// <summary>Table racine des fiches atelier (P3-6B).</summary>
    private const string WorkshopSheetsTableName = "WorkshopSheets";

    /// <summary>Table des lignes snapshot des fiches atelier (P3-6B).</summary>
    private const string WorkshopSheetItemsTableName = "WorkshopSheetItems";

    /// <summary>Suffixe de l'identifiant de la migration additive P3-6B (tables de fiche atelier).</summary>
    private const string AddWorkshopSheetsMigrationSuffix = "_AddWorkshopSheets";

    private static bool IsAddWorkshopSheetsMigration(string migrationId)
        => migrationId.EndsWith(AddWorkshopSheetsMigrationSuffix, StringComparison.Ordinal);

    /// <summary>Table des clients.</summary>
    private const string CustomersTableName = "Customers";

    /// <summary>Colonne d'archivage client (P3-2B).</summary>
    private const string IsArchivedColumnName = "IsArchived";

    /// <summary>Suffixe de l'identifiant de la migration additive P3-2B (archivage client + FK Restrict).</summary>
    private const string AddCustomerArchivingMigrationSuffix = "_AddCustomerArchivingAndProtectHistory";

    private static bool IsAddCustomerArchivingMigration(string migrationId)
        => migrationId.EndsWith(AddCustomerArchivingMigrationSuffix, StringComparison.Ordinal);

    /// <summary>Table des notifications.</summary>
    private const string NotificationsTableName = "Notifications";

    /// <summary>Colonne de résolution des alertes de stock bas (P3-8).</summary>
    private const string ResolvedAtColumnName = "ResolvedAt";

    /// <summary>Index unique filtré protégeant l'unicité des alertes de stock bas actives (P3-8).</summary>
    private const string ActiveLowStockUniqueIndexName = "idx_notifications_active_low_stock_unique";

    /// <summary>Suffixe de l'identifiant de la migration additive P3-8 (résolution des notifications).</summary>
    private const string AddNotificationResolutionMigrationSuffix = "_AddNotificationResolution";

    private static bool IsAddNotificationResolutionMigration(string migrationId)
        => migrationId.EndsWith(AddNotificationResolutionMigrationSuffix, StringComparison.Ordinal);

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

        // --- P3-8 : garantie inconditionnelle, quel que soit le chemin (fresh install / managé / adopté) ---
        // AddNotificationResolution est TOUJOURS dans l'historique une fois la préparation terminée sans erreur
        // (aucune migration en attente n'a été vérifiée ci-dessus). Un historique qui l'affirme sans que la colonne
        // et l'index unique filtré existent physiquement — schéma altéré à la main après coup, y compris sur une
        // base déjà gérée par migrations — ne doit jamais être accepté silencieusement.
        var (activeLowStockIndexExists, activeLowStockIndexValid) = InspectActiveLowStockUniqueIndex(context);
        if (!ColumnExists(context, NotificationsTableName, ResolvedAtColumnName)
            || !activeLowStockIndexExists
            || !activeLowStockIndexValid)
        {
            throw new DatabaseMigrationException(
                "Incohérence après préparation : la colonne Notifications.ResolvedAt ou l'index unique filtré " +
                "idx_notifications_active_low_stock_unique sont absents ou mal définis alors qu'aucune migration " +
                "n'est en attente (AddNotificationResolution devrait être appliquée). __EFMigrationsHistory ne " +
                "reflète pas le schéma réel.");
        }
    }

    /// <summary>
    /// Indique si une colonne existe physiquement dans la base (PRAGMA table_info). Utilisé pour distinguer une
    /// migration réellement reflétée par le schéma d'une migration à exécuter (cf. adoption historique).
    /// </summary>
    private static bool ColumnExists(OpticDbContext context, string tableName, string columnName)
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
            command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // Colonne 1 de PRAGMA table_info = nom de la colonne.
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    /// <summary>
    /// Inspecte physiquement l'index unique filtré des alertes de stock bas actives (P3-8) via
    /// <c>sqlite_master.sql</c> — PRAGMA <c>index_info</c> n'expose pas la clause <c>WHERE</c>, donc seule une
    /// lecture du texte SQL réel permet de distinguer un index correctement filtré d'un homonyme mal défini.
    /// </summary>
    /// <returns>
    /// <c>Exists</c> : un index de ce nom existe. <c>Valid</c> : il est <c>UNIQUE</c>, porte les trois colonnes
    /// attendues et filtre exactement sur les quatre termes requis (Type='LowStock', EntityType='Product',
    /// EntityId IS NOT NULL, ResolvedAt IS NULL).
    /// </returns>
    private static (bool Exists, bool Valid) InspectActiveLowStockUniqueIndex(OpticDbContext context)
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
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @name";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = ActiveLowStockUniqueIndexName;
            command.Parameters.Add(parameter);

            var sql = command.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(sql))
            {
                return (false, false);
            }

            var valid = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("\"Type\"", StringComparison.Ordinal)
                && sql.Contains("\"EntityType\"", StringComparison.Ordinal)
                && sql.Contains("\"EntityId\"", StringComparison.Ordinal)
                && sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase)
                && sql.Contains(NotificationTypes.LowStock, StringComparison.Ordinal)
                && sql.Contains(NotificationEntityTypes.Product, StringComparison.Ordinal)
                && sql.Contains("\"EntityId\" IS NOT NULL", StringComparison.Ordinal)
                && sql.Contains("\"ResolvedAt\" IS NULL", StringComparison.Ordinal);

            return (true, valid);
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
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
