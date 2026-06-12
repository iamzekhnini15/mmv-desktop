using Microsoft.Data.Sqlite;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Résultat d'une tentative de reprise de l'ancien fichier de base (P2A-1B).
/// </summary>
public sealed class LegacyDatabaseRecoveryResult
{
    /// <summary>Diagnostic de l'ancien fichier (ex. <c>mmv-optic.db</c>).</summary>
    public required HistoricalDatabaseDiagnosticResult LegacyDiagnostic { get; init; }

    /// <summary>Diagnostic du chemin courant (ex. <c>%LOCALAPPDATA%\ManageMyVision\mmv.db</c>).</summary>
    public required HistoricalDatabaseDiagnosticResult CurrentDiagnostic { get; init; }

    /// <summary>Décision appliquée.</summary>
    public HistoricalDatabaseDecision Decision { get; init; }

    /// <summary>Vrai si la copie ancien → courant a été réalisée.</summary>
    public bool CopyPerformed { get; init; }

    /// <summary>Vrai si un conflit ancien/courant a été détecté (les deux présents).</summary>
    public bool ConflictDetected { get; init; }

    /// <summary>Chemin de la sauvegarde créée avant copie (si applicable).</summary>
    public string? BackupPath { get; init; }

    /// <summary>Rapport avant/après lisible par le support (métadonnées techniques uniquement).</summary>
    public required HistoricalMigrationReport Report { get; init; }
}

/// <summary>
/// Reprise <b>contrôlée et sûre</b> de l'ancien fichier de base relatif <c>mmv-optic.db</c> vers le
/// chemin courant unique (<c>%LOCALAPPDATA%\ManageMyVision\mmv.db</c>) — résout le risque résiduel
/// P2A-1A #2 (bascule de chemin runtime). Garanties (P2A-1B) :
///
/// <list type="bullet">
/// <item>l'ancien fichier n'est <b>jamais</b> supprimé ni écrasé (lecture + copie uniquement) ;</item>
/// <item>une <b>sauvegarde</b> de l'ancien fichier est créée <b>avant</b> toute copie ;</item>
/// <item>la copie n'a lieu <b>que si</b> l'ancien fichier est une base SQLite valide et compatible
/// <b>et</b> qu'aucune base n'existe déjà au chemin courant (sinon : conflit, on conserve le courant) ;</item>
/// <item>un schéma ancien/incompatible est <b>refusé</b> proprement (intervention support) ;</item>
/// <item>chaque décision est <b>journalisée</b> et un <b>rapport avant/après</b> est produit ;</item>
/// <item>aucune perte silencieuse : comptes de lignes avant/après vérifiables.</item>
/// </list>
///
/// La logique est isolée dans l'Infrastructure (réutilisable hors UI, P2A-1A option 4 / futur installeur).
/// </summary>
public sealed class LegacyDatabaseRecoveryService
{
    private readonly IMigrationJournal _journal;
    private readonly SqliteHistoricalDatabaseDiagnostic _diagnostic;
    private readonly SqliteDatabaseManager _manager;

    public LegacyDatabaseRecoveryService(
        IMigrationJournal? journal = null,
        SqliteHistoricalDatabaseDiagnostic? diagnostic = null,
        SqliteDatabaseManager? manager = null)
    {
        _journal = journal ?? new MigrationJournal();
        _diagnostic = diagnostic ?? new SqliteHistoricalDatabaseDiagnostic();
        _manager = manager ?? new SqliteDatabaseManager(_journal);
    }

    /// <summary>
    /// Évalue et, si c'est sûr, réalise la reprise de l'ancien fichier <paramref name="legacyPath"/>
    /// vers <paramref name="currentPath"/>. Ne lève pas d'exception pour les cas attendus
    /// (absent / invalide / incompatible / conflit) : ils sont reflétés dans le résultat et le rapport.
    /// </summary>
    /// <param name="legacyPath">Chemin de l'ancien fichier (ex. <c>mmv-optic.db</c>).</param>
    /// <param name="currentPath">Chemin courant unique de la base.</param>
    /// <param name="contextFactory">
    /// Fabrique de <see cref="OpticDbContext"/> pour un chemin donné (injectable pour les tests).
    /// </param>
    public LegacyDatabaseRecoveryResult Recover(
        string legacyPath,
        string currentPath,
        Func<string, OpticDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath);

        var legacyDiagnostic = DiagnoseAt(legacyPath, contextFactory);
        var currentDiagnostic = DiagnoseAt(currentPath, contextFactory);
        SqliteConnection.ClearAllPools();

        _journal.Write(
            $"LEGACY diagnose legacy='{legacyPath}' decision={legacyDiagnostic.RecommendedDecision} " +
            $"valid={legacyDiagnostic.IsValidSqlite} app={legacyDiagnostic.HasApplicationTables} history={legacyDiagnostic.HasMigrationsHistory}");
        _journal.Write(
            $"LEGACY diagnose current='{currentPath}' exists={currentDiagnostic.FileExists} decision={currentDiagnostic.RecommendedDecision}");

        // 1) Aucun ancien fichier : rien à reprendre.
        if (!legacyDiagnostic.FileExists)
        {
            _journal.Write("LEGACY none: aucun ancien fichier à reprendre.");
            return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.NoDatabaseFound,
                copyPerformed: false, conflict: false, backupPath: null,
                finalResult: "Aucun ancien fichier mmv-optic.db détecté : rien à reprendre.");
        }

        // 2) Ancien fichier illisible / non-SQLite : refus propre, conservé intact.
        if (legacyDiagnostic.RecommendedDecision == HistoricalDatabaseDecision.RejectInvalidDatabase)
        {
            _journal.Write("LEGACY rejected: ancien fichier invalide (conservé, non recopié).");
            return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.RejectInvalidDatabase,
                copyPerformed: false, conflict: false, backupPath: null,
                finalResult: "Ancien fichier illisible ou non-SQLite : reprise refusée. Fichier conservé intact pour le support.");
        }

        // 3) Ancien fichier valide mais vide : rien d'utile à reprendre.
        if (!legacyDiagnostic.HasApplicationTables && !legacyDiagnostic.HasMigrationsHistory)
        {
            _journal.Write("LEGACY empty: ancien fichier valide mais vide (rien à reprendre).");
            return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.NoDatabaseFound,
                copyPerformed: false, conflict: false, backupPath: null,
                finalResult: "Ancien fichier SQLite valide mais vide : aucune donnée historique à reprendre.");
        }

        // 4) Schéma ancien / incompatible : refus contrôlé, intervention support.
        if (legacyDiagnostic.RecommendedDecision == HistoricalDatabaseDecision.ManualRepairRequired)
        {
            _journal.Write(
                $"LEGACY refused: schéma ancien incompatible ({legacyDiagnostic.SchemaDifferences.Count} divergence(s)) — reprise manuelle requise.");
            return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.ManualRepairRequired,
                copyPerformed: false, conflict: false, backupPath: null,
                finalResult: "Ancien schéma incompatible avec le modèle courant : reprise automatique refusée. " +
                             "Fichier conservé ; intervention support requise (voir divergences).");
        }

        // 5) Ancien fichier valide et compatible. Conflit si une base existe déjà au chemin courant.
        if (currentDiagnostic.FileExists)
        {
            _journal.Write("LEGACY conflict: une base existe déjà au chemin courant — ancien fichier conservé, non recopié.");
            return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.UseAsCurrentDatabase,
                copyPerformed: false, conflict: true, backupPath: null,
                finalResult: "Conflit ancien/nouveau : une base est déjà présente au chemin courant. " +
                             "Ancien fichier CONSERVÉ (ni recopié, ni supprimé) ; la base courante est utilisée. " +
                             "Fusion éventuelle = décision support.");
        }

        // 6) Reprise sûre : sauvegarde de l'ancien fichier, copie vers le chemin courant, adoption.
        return PerformControlledCopy(legacyPath, currentPath, contextFactory, legacyDiagnostic, currentDiagnostic);
    }

    private LegacyDatabaseRecoveryResult PerformControlledCopy(
        string legacyPath,
        string currentPath,
        Func<string, OpticDbContext> contextFactory,
        HistoricalDatabaseDiagnosticResult legacyDiagnostic,
        HistoricalDatabaseDiagnosticResult currentDiagnostic)
    {
        var errors = new List<string>();

        // 6a) Sauvegarde AVANT toute mutation (de l'ancien fichier, restaurable).
        var backupPath = _manager.Backup(legacyPath);
        _journal.Write($"LEGACY backup created '{backupPath}'");

        // 6b) Copie contrôlée ancien → courant (le courant est absent : pas d'écrasement).
        SqliteDatabasePathResolver.EnsureDirectoryExists(currentPath);
        File.Copy(legacyPath, currentPath, overwrite: false);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = legacyPath + suffix;
            if (File.Exists(sidecar))
            {
                File.Copy(sidecar, currentPath + suffix, overwrite: false);
            }
        }

        _journal.Write($"LEGACY copy '{legacyPath}' -> '{currentPath}'");
        SqliteConnection.ClearAllPools();

        // 6c) Adoption de la copie au chemin courant (baseline + vérification), via le cycle de vie P2A-1A.
        HistoricalDatabaseDiagnosticResult afterDiagnostic;
        try
        {
            using (var currentContext = contextFactory(currentPath))
            {
                _manager.PrepareDatabase(currentContext);
            }
            SqliteConnection.ClearAllPools();

            afterDiagnostic = DiagnoseAt(currentPath, contextFactory);
            SqliteConnection.ClearAllPools();
        }
        catch (DatabaseMigrationException ex)
        {
            // Très improbable (l'ancien a été diagnostiqué compatible) : refus propre, courant à nettoyer par support.
            errors.Add(ex.Message);
            _journal.Write($"LEGACY copy adoption FAILED: {ex.Message}");
            return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.ManualRepairRequired,
                copyPerformed: true, conflict: false, backupPath: backupPath,
                finalResult: "Copie réalisée mais adoption refusée au chemin courant : intervention support requise. " +
                             "Ancien fichier et sauvegarde conservés.",
                errors: errors,
                rowCountsAfter: new Dictionary<string, long>());
        }

        _journal.Write("LEGACY recovery done: ancien fichier repris au chemin courant (sauvegarde conservée).");
        return Build(legacyDiagnostic, currentDiagnostic, HistoricalDatabaseDecision.CopyLegacyDatabaseToCurrentPath,
            copyPerformed: true, conflict: false, backupPath: backupPath,
            finalResult: "Reprise réussie : ancien fichier recopié vers le chemin courant et adopté (migrations inscrites). " +
                         "Comptes de lignes conservés ; ancien fichier et sauvegarde conservés.",
            errors: errors,
            rowCountsAfter: afterDiagnostic.TableRowCounts);
    }

    private HistoricalDatabaseDiagnosticResult DiagnoseAt(string path, Func<string, OpticDbContext> contextFactory)
    {
        using var context = contextFactory(path);
        return _diagnostic.Diagnose(context);
    }

    private LegacyDatabaseRecoveryResult Build(
        HistoricalDatabaseDiagnosticResult legacyDiagnostic,
        HistoricalDatabaseDiagnosticResult currentDiagnostic,
        HistoricalDatabaseDecision decision,
        bool copyPerformed,
        bool conflict,
        string? backupPath,
        string finalResult,
        IReadOnlyList<string>? errors = null,
        IReadOnlyDictionary<string, long>? rowCountsAfter = null)
    {
        var report = new HistoricalMigrationReport
        {
            Decision = decision,
            SourceFile = legacyDiagnostic.DatabasePath,
            TargetFile = currentDiagnostic.DatabasePath,
            BackupCreated = backupPath is not null,
            BackupPath = backupPath,
            CopyPerformed = copyPerformed,
            ConflictDetected = conflict,
            Tables = legacyDiagnostic.TableRowCounts.Keys.ToList(),
            DivergentColumns = legacyDiagnostic.SchemaDifferences,
            RowCountsBefore = legacyDiagnostic.TableRowCounts,
            RowCountsAfter = rowCountsAfter ?? legacyDiagnostic.TableRowCounts,
            Errors = errors ?? Array.Empty<string>(),
            FinalResult = finalResult
        };

        return new LegacyDatabaseRecoveryResult
        {
            LegacyDiagnostic = legacyDiagnostic,
            CurrentDiagnostic = currentDiagnostic,
            Decision = decision,
            CopyPerformed = copyPerformed,
            ConflictDetected = conflict,
            BackupPath = backupPath,
            Report = report
        };
    }
}
