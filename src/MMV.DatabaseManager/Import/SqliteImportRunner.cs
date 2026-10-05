using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using MMV.DatabaseManager.Backup;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Import;

/// <summary>Demande d'import SQLite → PostgreSQL.</summary>
/// <param name="SourcePath">Copie à froid de la base SQLite.</param>
/// <param name="SourceTimeZone">Fuseau du magasin d'origine (identifiant IANA ou Windows) — aucun défaut (ADR-004 §8).</param>
/// <param name="Operator">Référence d'opérateur (H13).</param>
/// <param name="BackupReference">Manifeste de la sauvegarde vérifiée de la cible (DP-10, B-4).</param>
/// <param name="ReportPath">Fichier de rapport, créé neuf.</param>
/// <param name="DryRun">Tout exécuter et vérifier, puis annuler la transaction.</param>
/// <param name="Wait">Attente bornée du verrou.</param>
/// <param name="ApplicationVersion">Version de l'outil.</param>
public sealed record SqliteImportRequest(
    string SourcePath,
    string SourceTimeZone,
    string Operator,
    string BackupReference,
    string ReportPath,
    bool DryRun,
    TimeSpan Wait,
    string ApplicationVersion);

/// <summary>Issue d'un import.</summary>
public sealed record SqliteImportResult(MigrationExitCode ExitCode, Guid RunId, string Message);

/// <summary>
/// Import contrôlé d'une base SQLite existante dans une base PostgreSQL <b>neuve</b> (P4-7, O11, critère de sortie 7).
/// Ordre <b>normatif</b> :
/// <code>
///  0  options, fuseau, rapport neuf                                    échec ⇒ 10, aucun contact
///  1  source : copie à froid, intégrité, clés étrangères, historique
///     SQLite exact, tables et colonnes, conversion de CHAQUE valeur    échec ⇒ 24, aucun contact serveur
///  2  sauvegarde vérifiée de la cible (preuve P4-9)                     échec ⇒ 11, aucune écriture
///  3  verrou de migration (même clé que migrate : exclusion mutuelle)   échec ⇒ 12, aucune écriture
///  4  sauvegarde ⇔ base courante (B-4), schéma à jour, métadonnée       11 / 25, aucune écriture
///  5  maintenance posée (postes bloqués au démarrage)                   levée en finally
///  6  UNE transaction : verrous ACCESS EXCLUSIVE, cible vierge          sinon ⇒ 25, annulée
///     (seules les lignes HasData), remplacement des HasData, import
///     dans l'ordre des clés étrangères, identifiants conservés,
///     séquences recalées, vérification (lignes, ligne à ligne,
///     totaux numériques relus)                                         échec ⇒ 26, annulée, cible relue
///  7  COMMIT (ou ROLLBACK en --dry-run), puis relecture des comptes     écart ⇒ 14 → restore-backup (R-9)
/// </code>
/// Les clés étrangères, l'unicité, les contraintes et index physiques sont <b>vérifiés par le serveur</b> à chaque
/// insertion (contraintes immédiates) : aucune n'est désactivée. Aucun nouvel essai automatique (D-15).
/// </summary>
public sealed class SqliteImportRunner
{
    private const string HistoryTable = "__EFMigrationsHistory";
    private const int ParametersPerStatement = 16000;

    private readonly MigrationPorts _ports;
    private readonly DbConnection _connection;
    private readonly ImportPlan _plan;
    private readonly IBackupVerification _backupVerification;
    private readonly IMigrationJournal _localTrace;
    private readonly Func<CancellationToken, Task<DbConnection>> _openFreshConnection;
    private readonly Func<string, CancellationToken, Task>? _afterTable;

    /// <param name="ports">Ports de la session de l'outil (verrou, migrations, métadonnée, empreinte).</param>
    /// <param name="connection">Connexion de contrôle de la session, qui tient le verrou.</param>
    /// <param name="plan">Plan tiré du modèle EF PostgreSQL.</param>
    /// <param name="backupVerification">En production : <see cref="ProofBackupVerification"/> seulement.</param>
    /// <param name="localTrace">Trace locale, sans secret.</param>
    /// <param name="openFreshConnection">Connexion neuve pour relire la cible si la session est perdue.</param>
    /// <param name="afterTable">Point d'observation des tests (après chaque table, dans la transaction).</param>
    public SqliteImportRunner(
        MigrationPorts ports,
        DbConnection connection,
        ImportPlan plan,
        IBackupVerification backupVerification,
        IMigrationJournal localTrace,
        Func<CancellationToken, Task<DbConnection>> openFreshConnection,
        Func<string, CancellationToken, Task>? afterTable = null)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _backupVerification = backupVerification ?? throw new ArgumentNullException(nameof(backupVerification));
        _localTrace = localTrace ?? throw new ArgumentNullException(nameof(localTrace));
        _openFreshConnection = openFreshConnection ?? throw new ArgumentNullException(nameof(openFreshConnection));
        _afterTable = afterTable;
    }

    public async Task<SqliteImportResult> RunAsync(SqliteImportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = Guid.NewGuid();
        _localTrace.Write($"OPEN import {runId} version {request.ApplicationVersion} opérateur '{request.Operator}' " +
                          $"source '{request.SourcePath}' fuseau '{request.SourceTimeZone}' sauvegarde '{request.BackupReference}'" +
                          (request.DryRun ? " (dry-run)" : string.Empty));

        // 0 — options : aucun contact.
        var refusal = CheckOptions(request, out var zone);
        if (refusal is not null)
        {
            return Close(runId, null, null, MigrationExitCode.InvalidArguments, refusal);
        }

        try
        {
            ImportReport.Reserve(request.ReportPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Close(runId, null, null, MigrationExitCode.InvalidArguments,
                $"Rapport '{request.ReportPath}' non créable (fichier existant ou dossier inaccessible) : un rapport n'est jamais écrasé.");
        }

        var report = new ImportReport
        {
            RunId = runId,
            ToolVersion = request.ApplicationVersion,
            Operator = request.Operator,
            DryRun = request.DryRun,
            StartedAtUtc = DateTime.UtcNow,
            SourcePath = Path.GetFullPath(request.SourcePath),
            SourceTimeZone = zone!.Id,
            BackupReference = request.BackupReference
        };

        string outcome;
        MigrationExitCode code;
        string message;
        try
        {
            (code, outcome, message) = await ExecuteAsync(runId, request, zone, report, cancellationToken);
        }
        catch (Exception exception)
        {
            (code, outcome, message) = (MigrationExitCode.ImportFailed, "failed", $"Échec inattendu : {ServerCommand.Describe(exception)}");
        }

        return Close(runId, report, request.ReportPath, code, message, outcome);
    }

    private SqliteImportResult Close(Guid runId, ImportReport? report, string? reportPath, MigrationExitCode code, string message,
        string outcome = "refused")
    {
        if (report is not null && reportPath is not null)
        {
            report.Outcome = outcome;
            report.ExitCode = (int)code;
            report.Message = message;
            report.FinishedAtUtc = DateTime.UtcNow;
            try
            {
                report.Save(reportPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _localTrace.Write($"import {runId} : écriture du rapport en échec ({exception.Message})");
                message += $" — RAPPORT NON ÉCRIT ({exception.Message})";
            }
        }

        _localTrace.Write($"CLOSE import {runId} code {(int)code} {message}");
        return new SqliteImportResult(code, runId, message);
    }

    private static string? CheckOptions(SqliteImportRequest request, out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(request.Operator))
        {
            return "--operator est obligatoire (H13).";
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath) || string.IsNullOrWhiteSpace(request.ReportPath))
        {
            return "--source et --report sont obligatoires.";
        }

        if (string.IsNullOrWhiteSpace(request.BackupReference))
        {
            return "--backup-ref est obligatoire : sauvegarde vérifiée de la cible (DP-10, R-9).";
        }

        if (string.IsNullOrWhiteSpace(request.SourceTimeZone))
        {
            return "--source-time-zone est obligatoire : fuseau du magasin d'origine, aucun défaut (ADR-004 décision 8).";
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(request.SourceTimeZone.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"Fuseau '{request.SourceTimeZone}' inconnu de ce système.";
        }

        return ApplicationVersion.TryParse(request.ApplicationVersion, out _)
            ? null
            : $"Version applicative '{request.ApplicationVersion}' invalide.";
    }

    private async Task<(MigrationExitCode, string, string)> ExecuteAsync(
        Guid runId, SqliteImportRequest request, TimeZoneInfo zone, ImportReport report, CancellationToken cancellationToken)
    {
        // 1 — source, sans aucun contact serveur.
        var cold = SqliteImportSource.CheckCold(request.SourcePath);
        if (cold is not null)
        {
            return Rejected(cold);
        }

        (report.SourceSha256, report.SourceBytes) = await SqliteImportSource.HashAsync(request.SourcePath, cancellationToken);

        SqliteImportSource source;
        try
        {
            source = await SqliteImportSource.OpenAsync(request.SourcePath, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            return Rejected($"base SQLite illisible : {ServerCommand.Describe(exception)}");
        }

        await using (source)
        {
            string? sourceRefusal;
            try
            {
                sourceRefusal = await CheckSourceAsync(source, report, zone, cancellationToken);
            }
            catch (DbException exception)
            {
                sourceRefusal = $"base SQLite illisible : {ServerCommand.Describe(exception)}";
            }

            if (sourceRefusal is not null)
            {
                return Rejected(sourceRefusal);
            }

            _localTrace.Write($"import {runId} : source vérifiée ({report.SourceSha256}), " +
                              $"{report.Tables.Sum(t => t.SourceRows)} ligne(s), aucune valeur refusée");

            // 2 — sauvegarde vérifiée, AVANT le verrou.
            var backup = await _backupVerification.VerifyAsync(request.BackupReference, cancellationToken);
            if (!backup.IsVerified)
            {
                return (MigrationExitCode.BackupNotVerified, "refused", $"Sauvegarde non vérifiée : {backup.Reason}");
            }

            // 3 — verrou de migration.
            bool acquired;
            try
            {
                acquired = await _ports.Lock.TryAcquireAsync(request.Wait, cancellationToken);
            }
            catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
            {
                return (MigrationExitCode.ServerUnreachable, "refused", "Serveur injoignable ou authentification refusée.");
            }

            if (!acquired)
            {
                return (MigrationExitCode.LockNotAcquired, "refused",
                    "Verrou de migration détenu par une autre exécution (migrate ou import) : aucune écriture effectuée.");
            }

            try
            {
                return await UnderLockAsync(runId, request, source, zone, backup, report, cancellationToken);
            }
            finally
            {
                try
                {
                    await _ports.Lock.ReleaseAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _localTrace.Write($"import {runId} : libération du verrou en échec ({ServerCommand.Describe(exception)}) — " +
                                      "le serveur le libère en fin de session");
                }
            }
        }

        static (MigrationExitCode, string, string) Rejected(string reason) =>
            (MigrationExitCode.ImportSourceRejected, "source-rejected", $"Source refusée : {reason} — aucune écriture effectuée.");
    }

    /// <summary>Contrôles de la source et conversion à blanc de <b>chaque</b> valeur ; remplit le rapport.</summary>
    private async Task<string?> CheckSourceAsync(SqliteImportSource source, ImportReport report, TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var integrity = await source.IntegrityProblemAsync(cancellationToken);
        if (integrity is not null)
        {
            return $"PRAGMA integrity_check en échec : {integrity}";
        }

        var applied = await source.AppliedMigrationsAsync(cancellationToken);
        report.SourceMigrations = applied;
        if (!applied.SequenceEqual(source.KnownMigrations, StringComparer.Ordinal))
        {
            return $"historique SQLite [{string.Join(", ", applied)}] différent des {source.KnownMigrations.Count} migrations " +
                   "connues de cette version — ouvrir la base avec la version courante de MMV, puis en refaire une copie à froid";
        }

        var orphans = await source.ForeignKeyViolationsAsync(cancellationToken);
        if (orphans.Count > 0)
        {
            return $"{orphans.Count} ligne(s) orpheline(s) (PRAGMA foreign_key_check) : {string.Join(" ; ", orphans.Take(10))}";
        }

        var expected = _plan.Tables.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var present = await source.TablesAsync(cancellationToken);
        var missing = expected.Except(present, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            return $"table(s) absente(s) de la source : {string.Join(", ", missing)}";
        }

        foreach (var unknown in present.Where(t => !expected.Contains(t)))
        {
            if (unknown == HistoryTable)
            {
                report.ExcludedSourceTables.Add($"{HistoryTable} — jamais importé (ADR-005 §5.8, O11)");
                continue;
            }

            var rows = await source.CountAsync(unknown, cancellationToken);
            if (rows > 0)
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"table inconnue du modèle '{unknown}' porteuse de {rows} ligne(s) : ses données ne seraient pas importées");
            }

            report.ExcludedSourceTables.Add($"{unknown} — inconnue du modèle, vide");
        }

        foreach (var table in _plan.Tables)
        {
            var columns = await source.ColumnsAsync(table.Name, cancellationToken);
            var planned = table.Columns.Select(c => c.Name).ToArray();
            if (!columns.OrderBy(c => c, StringComparer.Ordinal).SequenceEqual(planned.OrderBy(c => c, StringComparer.Ordinal), StringComparer.Ordinal))
            {
                return $"colonnes de '{table.Name}' différentes du modèle : source [{string.Join(", ", columns)}], " +
                       $"modèle [{string.Join(", ", planned)}]";
            }
        }

        foreach (var table in _plan.Tables)
        {
            await ProfileTableAsync(source, table, zone, report, cancellationToken);
        }

        return report.RejectedValueCount == 0
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"{report.RejectedValueCount} valeur(s) non importable(s), détail au rapport — première : " +
                $"{report.RejectedValues[0].Table} [{report.RejectedValues[0].Key}] {report.RejectedValues[0].Column} : {report.RejectedValues[0].Detail}");
    }

    private static async Task ProfileTableAsync(SqliteImportSource source, ImportTable table, TimeZoneInfo zone, ImportReport report,
        CancellationToken cancellationToken)
    {
        var rows = 0L;
        var numeric = table.Columns.Select(_ => (Source: 0m, Imported: 0m, Rounded: 0L)).ToArray();
        var civil = table.Columns.Select(_ => (Readable: 0L, Truncated: 0L)).ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in source.RowsAsync(table, cancellationToken))
        {
            rows++;
            var key = KeyOf(table, row);
            if (!keys.Add(key))
            {
                report.AddRejected(new ImportFinding(table.Name, key, "(clé primaire)", "clé primaire en double"));
            }

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var column = table.Columns[i];
                var conversion = ImportValues.Convert(column, row[i], zone);
                if (!conversion.IsValid)
                {
                    report.AddRejected(new ImportFinding(table.Name, key, column.Name, conversion.Error!));
                    continue;
                }

                if (column.Kind == ImportColumnKind.Numeric && conversion.Value is decimal imported)
                {
                    numeric[i] = (numeric[i].Source + conversion.SourceDecimal!.Value, numeric[i].Imported + imported,
                        numeric[i].Rounded + (conversion.Note == ImportNote.Rounded ? 1 : 0));
                }

                switch (conversion.Note)
                {
                    case ImportNote.AmbiguousLocalTime:
                        report.AddAmbiguous(new ImportFinding(table.Name, key, column.Name,
                            $"'{row[i]}' ambiguë dans {zone.Id} : décalage standard retenu → {ImportValues.Canonical(column, conversion.Value)}"));
                        break;
                    case ImportNote.SubMicrosecondTruncated:
                        report.SubMicrosecondTruncatedInstants++;
                        break;
                    case ImportNote.CivilDateReadable:
                        civil[i].Readable++;
                        break;
                    case ImportNote.CivilDateTruncated:
                        civil[i].Truncated++;
                        break;
                }
            }
        }

        report.Tables.Add(new ImportTableReport(table.Name, rows, 0, 0, false));
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            if (column.Kind == ImportColumnKind.Numeric)
            {
                report.NumericColumns.Add(new ImportNumericReport(table.Name, column.Name, numeric[i].Source, numeric[i].Imported,
                    numeric[i].Imported - numeric[i].Source, numeric[i].Rounded, null));
            }

            if (column.Kind == ImportColumnKind.CivilDate)
            {
                report.CivilDates.Add(new ImportCivilDateReport(table.Name, column.Name, civil[i].Readable, civil[i].Truncated));
            }
        }
    }

    private async Task<(MigrationExitCode, string, string)> UnderLockAsync(
        Guid runId, SqliteImportRequest request, SqliteImportSource source, TimeZoneInfo zone, BackupVerificationResult backup,
        ImportReport report, CancellationToken cancellationToken)
    {
        // 4 — B-4 : la sauvegarde vérifiée est celle de CETTE base, dans son état ACTUEL. Lecture seule.
        DatabaseFingerprint before;
        try
        {
            before = await _ports.State.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return (MigrationExitCode.ServerUnreachable, "refused", "Serveur injoignable ou authentification refusée.");
        }

        report.TargetDatabase = before.Database;
        report.TargetDatabaseOid = before.DatabaseOid;
        report.TargetSystemIdentifier = before.SystemIdentifier;
        report.TargetMigrations = before.AppliedMigrations;

        var current = _backupVerification.ConfirmCurrent(backup, live: before);
        if (!current.IsVerified)
        {
            return (MigrationExitCode.BackupNotVerified, "refused", $"Sauvegarde non vérifiée : {current.Reason}");
        }

        report.BackupId = current.Manifest?.BackupId.ToString("D");

        var known = _ports.Migrator.KnownMigrations.OrderBy(m => m, StringComparer.Ordinal).ToArray();
        if (!before.AppliedMigrations.SequenceEqual(known, StringComparer.Ordinal))
        {
            return (MigrationExitCode.ImportTargetRefused, "target-refused",
                $"Cible non à jour : migrations appliquées [{string.Join(", ", before.AppliedMigrations)}], connues " +
                $"[{string.Join(", ", known)}] — exécuter migrate d'abord. Aucune écriture effectuée.");
        }

        var metadata = await _ports.Metadata.ReadAsync(cancellationToken);
        if (metadata.Status != ServerMetadataStatus.Present || metadata.SchemaVersion is null)
        {
            return (MigrationExitCode.ImportTargetRefused, "target-refused",
                $"Métadonnée de compatibilité absente ou incomplète ({metadata.Status}) : base non préparée par migrate. Aucune écriture effectuée.");
        }

        // 5 — maintenance : un poste qui démarre est bloqué proprement, au lieu d'attendre les verrous de table.
        var stale = await _ports.Metadata.ClearStaleMaintenanceAsync(runId, cancellationToken);
        if (stale is { } marker)
        {
            _localTrace.Write(string.Create(CultureInfo.InvariantCulture,
                $"import {runId} : marqueur de maintenance périmé ({marker:o}) nettoyé sous verrou"));
        }

        await _ports.Metadata.BeginMaintenanceAsync(runId, cancellationToken);
        try
        {
            return await TransactionAsync(runId, request, source, zone, before, report, cancellationToken);
        }
        finally
        {
            try
            {
                await _ports.Metadata.EndMaintenanceAsync(runId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _localTrace.Write($"import {runId} : levée de la maintenance en échec ({ServerCommand.Describe(exception)}) — " +
                                  "le marqueur reste posé jusqu'à la prochaine exécution sous verrou");
            }
        }
    }

    private async Task<(MigrationExitCode, string, string)> TransactionAsync(
        Guid runId, SqliteImportRequest request, SqliteImportSource source, TimeZoneInfo zone, DatabaseFingerprint before,
        ImportReport report, CancellationToken cancellationToken)
    {
        DbTransaction transaction = await _connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var stage = "verrouillage des tables";
        try
        {
            await ExecuteAsync(transaction, string.Create(CultureInfo.InvariantCulture,
                $"SET LOCAL lock_timeout = '{(int)MigrationRunner.DefaultLockTimeout.TotalMilliseconds}ms'"), cancellationToken);
            await ExecuteAsync(transaction,
                $"LOCK TABLE {string.Join(", ", _plan.Tables.Select(t => Quote(t.Name)))} IN ACCESS EXCLUSIVE MODE", cancellationToken);

            stage = "contrôle de la cible vierge";
            var notFresh = await CheckFreshAsync(transaction, cancellationToken);
            if (notFresh is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (MigrationExitCode.ImportTargetRefused, "target-refused",
                    $"Cible non vierge : {notFresh} — import déjà effectué ou données présentes. Aucune écriture effectuée.");
            }

            var seedReplaced = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var table in _plan.Tables.Reverse().Where(t => t.SeedKeys.Count > 0))
            {
                stage = $"retrait des données initiales de {table.Name}";
                seedReplaced[table.Name] = await ExecuteAsync(transaction, $"DELETE FROM {Quote(table.Name)}", cancellationToken);
            }

            for (var t = 0; t < _plan.Tables.Count; t++)
            {
                var table = _plan.Tables[t];
                stage = $"import de {table.Name}";
                var rows = new List<object?[]>();
                await foreach (var raw in source.RowsAsync(table, cancellationToken))
                {
                    rows.Add(ConvertRow(table, raw, zone));
                }

                await InsertAsync(transaction, table, rows, cancellationToken);

                stage = $"vérification de {table.Name}";
                var mismatch = await CompareAsync(transaction, table, rows, cancellationToken);
                if (mismatch is not null)
                {
                    throw new ImportVerificationException($"{table.Name} : {mismatch}");
                }

                var index = report.Tables.FindIndex(r => r.Table == table.Name);
                report.Tables[index] = report.Tables[index] with
                {
                    SeedRowsReplaced = seedReplaced.GetValueOrDefault(table.Name),
                    ImportedRows = rows.Count,
                    RowsVerified = true
                };

                if (_afterTable is not null)
                {
                    await _afterTable(table.Name, cancellationToken);
                }
            }

            stage = "recalage des séquences d'identité";
            report.IdentitySequencesResynchronized = await ResynchronizeIdentitiesAsync(transaction, cancellationToken);

            stage = "réconciliation des totaux numériques";
            await ReconcileTotalsAsync(transaction, report, cancellationToken);

            if (request.DryRun)
            {
                await transaction.RollbackAsync(cancellationToken);
                var unchanged = await VerifyUnchangedAsync(before, report);
                return unchanged == true
                    ? (MigrationExitCode.Success, "dry-run-rolled-back",
                        $"Dry-run : {Imported(report)} ligne(s) importée(s) et vérifiée(s) puis transaction annulée ; cible relue, inchangée.")
                    : (MigrationExitCode.ImportFailed, "failed",
                        "Dry-run annulé mais la cible relue diffère de l'état initial : restore-backup de la sauvegarde citée (R-9).");
            }

            stage = "validation";
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollback)
            {
                _localTrace.Write($"import {runId} : ROLLBACK non confirmé ({ServerCommand.Describe(rollback)}) — " +
                                  "le serveur annule toute transaction non validée d'une session perdue");
            }

            var unchanged = await VerifyUnchangedAsync(before, report);
            var cause = exception is ImportVerificationException ? exception.Message : ServerCommand.Describe(exception);
            return (MigrationExitCode.ImportFailed, "failed-rolled-back",
                $"Import annulé pendant « {stage} » : {cause} — " + unchanged switch
                {
                    true => "cible relue, identique à l'état d'avant import.",
                    false => "CIBLE RELUE DIFFÉRENTE de l'état d'avant import : restore-backup de la sauvegarde citée (R-9).",
                    null => "cible non relue (serveur injoignable) : vérifier par status ; retour arrière = restore-backup (R-9)."
                });
        }
        finally
        {
            await transaction.DisposeAsync();
        }

        // 7 — après validation : relecture des comptes sur la base réelle.
        var after = await _ports.State.ReadAsync(cancellationToken);
        var differences = report.Tables
            .Select(r => (r.Table, r.SourceRows, Actual: after.Tables.SingleOrDefault(t => t.Schema == "public" && t.Table == r.Table)?.Rows))
            .Where(r => r.Actual != r.SourceRows)
            .Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Table} ({r.SourceRows} → {r.Actual})"))
            .ToArray();
        if (differences.Length > 0)
        {
            return (MigrationExitCode.VerificationFailed, "committed-verification-failed",
                $"Import validé mais relecture divergente : {string.Join(", ", differences)} — retour arrière : restore-backup (R-9).");
        }

        return (MigrationExitCode.Success, "imported",
            $"Import validé : {Imported(report)} ligne(s) dans {report.Tables.Count} table(s), relues après validation. " +
            "La sauvegarde citée reste le point de retour (restore-backup, R-9) ; une nouvelle sauvegarde est requise avant toute migration.");
    }

    private static object?[] ConvertRow(ImportTable table, object?[] raw, TimeZoneInfo zone)
    {
        var converted = new object?[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            var conversion = ImportValues.Convert(table.Columns[i], raw[i], zone);
            converted[i] = conversion.IsValid
                ? conversion.Value
                : throw new ImportVerificationException(
                    $"{table.Name}.{table.Columns[i].Name} : valeur devenue non importable depuis le contrôle ({conversion.Error})");
        }

        return converted;
    }

    private async Task<string?> CheckFreshAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        foreach (var table in _plan.Tables)
        {
            var keys = new List<string>();
            await using var command = Command(transaction,
                $"SELECT {string.Join(", ", table.KeyColumns.Select(c => Quote(c.Name)))} FROM {Quote(table.Name)}");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new object?[table.KeyOrdinals.Count];
                reader.GetValues(row!);
                keys.Add(ImportPlan.KeyText(table.KeyColumns.Select((c, i) => ImportValues.Canonical(c, row[i]))));
            }

            if (!keys.Order(StringComparer.Ordinal).SequenceEqual(table.SeedKeys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                return string.Create(CultureInfo.InvariantCulture,
                    $"table {table.Name} : {keys.Count} ligne(s), {table.SeedKeys.Count} ligne(s) de données initiales attendue(s)");
            }
        }

        return null;
    }

    private async Task InsertAsync(DbTransaction transaction, ImportTable table, List<object?[]> rows, CancellationToken cancellationToken)
    {
        var columns = string.Join(", ", table.Columns.Select(c => Quote(c.Name)));
        var perStatement = Math.Max(1, ParametersPerStatement / table.Columns.Count);
        for (var offset = 0; offset < rows.Count; offset += perStatement)
        {
            await using var command = Command(transaction, string.Empty);
            var sql = new StringBuilder($"INSERT INTO {Quote(table.Name)} ({columns}) VALUES ");
            var n = 0;
            foreach (var row in rows.Skip(offset).Take(perStatement))
            {
                sql.Append(n == 0 ? "(" : ", (");
                for (var i = 0; i < row.Length; i++)
                {
                    var name = "p" + n.ToString(CultureInfo.InvariantCulture) + "_" + i.ToString(CultureInfo.InvariantCulture);
                    sql.Append(i == 0 ? string.Empty : ", ").Append("CAST(@").Append(name).Append(" AS ").Append(table.Columns[i].StoreType).Append(')');
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = row[i] ?? DBNull.Value;
                    command.Parameters.Add(parameter);
                }

                sql.Append(')');
                n++;
            }

            command.CommandText = sql.ToString();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>Relecture de la table dans l'ordre de la clé (<c>COLLATE "C"</c>) et comparaison valeur à valeur.</summary>
    private async Task<string?> CompareAsync(DbTransaction transaction, ImportTable table, List<object?[]> expected,
        CancellationToken cancellationToken)
    {
        var order = string.Join(", ", table.KeyColumns.Select(c =>
            c.Kind == ImportColumnKind.Text ? Quote(c.Name) + " COLLATE \"C\"" : Quote(c.Name)));
        await using var command = Command(transaction,
            $"SELECT {string.Join(", ", table.Columns.Select(c => Quote(c.Name)))} FROM {Quote(table.Name)} ORDER BY {order}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var index = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (index >= expected.Count)
            {
                return "ligne(s) en trop dans la cible";
            }

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var column = table.Columns[i];
                var actual = ImportValues.Canonical(column, reader.IsDBNull(i) ? null : reader.GetValue(i));
                var wanted = ImportValues.Canonical(column, expected[index][i]);
                if (!string.Equals(actual, wanted, StringComparison.Ordinal))
                {
                    return $"ligne [{KeyOfConverted(table, expected[index])}] colonne {column.Name} : attendu '{wanted}', relu '{actual}'";
                }
            }

            index++;
        }

        return index == expected.Count
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{expected.Count - index} ligne(s) manquante(s) dans la cible");
    }

    private async Task<int> ResynchronizeIdentitiesAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        var statements = new List<string>();
        await using (var command = Command(transaction,
                         "SELECT format('SELECT setval(pg_get_serial_sequence(%L, %L), coalesce((SELECT max(%I) FROM %I.%I), 0) + 1, false)', " +
                         "format('%I.%I', n.nspname, c.relname), a.attname, a.attname, n.nspname, c.relname) " +
                         "FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid = a.attrelid " +
                         "JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
                         "WHERE n.nspname = 'public' AND a.attidentity IN ('a', 'd') AND a.attnum > 0 AND NOT a.attisdropped " +
                         "ORDER BY c.relname, a.attname"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                statements.Add(reader.GetString(0));
            }
        }

        foreach (var statement in statements)
        {
            await using var command = Command(transaction, statement);
            await command.ExecuteScalarAsync(cancellationToken);
        }

        return statements.Count;
    }

    private async Task ReconcileTotalsAsync(DbTransaction transaction, ImportReport report, CancellationToken cancellationToken)
    {
        for (var i = 0; i < report.NumericColumns.Count; i++)
        {
            var column = report.NumericColumns[i];
            await using var command = Command(transaction,
                $"SELECT coalesce(sum({Quote(column.Column)}), 0) FROM {Quote(column.Table)}");
            var server = System.Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            report.NumericColumns[i] = column with { ServerTotal = server };
            if (server != column.ImportedTotal)
            {
                throw new ImportVerificationException(string.Create(CultureInfo.InvariantCulture,
                    $"total {column.Table}.{column.Column} relu {server}, attendu {column.ImportedTotal}"));
            }
        }
    }

    /// <summary>
    /// Relit la cible après annulation et la compare à l'état d'avant import (historique, tables, nombres de lignes).
    /// Session de contrôle d'abord ; si elle est perdue, connexion neuve. <c>null</c> : cible non relue.
    /// </summary>
    private async Task<bool?> VerifyUnchangedAsync(DatabaseFingerprint before, ImportReport report)
    {
        DatabaseFingerprint? after = null;
        try
        {
            after = await _ports.State.ReadAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            try
            {
                await using var fresh = await _openFreshConnection(CancellationToken.None);
                after = await new DatabaseFingerprintReader(fresh).ReadAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                _localTrace.Write($"relecture de la cible impossible ({ServerCommand.Describe(exception)})");
            }
        }

        bool? unchanged = after is null
            ? null
            : after.AppliedMigrations.SequenceEqual(before.AppliedMigrations, StringComparer.Ordinal)
              && after.Tables.Select(t => (t.Schema, t.Table, t.Rows)).SequenceEqual(before.Tables.Select(t => (t.Schema, t.Table, t.Rows)));
        report.TargetUnchangedVerified = unchanged;
        return unchanged;
    }

    private DbCommand Command(DbTransaction transaction, string sql)
    {
        var command = ServerCommand.Create(_connection, sql);
        command.Transaction = transaction;
        return command;
    }

    private async Task<int> ExecuteAsync(DbTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = Command(transaction, sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long Imported(ImportReport report) => report.Tables.Sum(t => t.ImportedRows);

    private static string KeyOf(ImportTable table, object?[] raw) =>
        ImportPlan.KeyText(table.KeyOrdinals.Select(i => raw[i] switch
        {
            null => "∅",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString() ?? string.Empty
        }));

    private static string KeyOfConverted(ImportTable table, object?[] converted) =>
        ImportPlan.KeyText(table.KeyOrdinals.Select(i => ImportValues.Canonical(table.Columns[i], converted[i])));

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private sealed class ImportVerificationException(string message) : Exception(message);
}
