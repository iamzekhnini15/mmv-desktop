using System.Globalization;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Locking;
using MMV.DatabaseManager.Permissions;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager;

/// <summary>Ports serveur de l'outil, composés par <see cref="PostgreSqlMigrationSession"/> en production.</summary>
public sealed record MigrationPorts(
    IMigrationLock Lock,
    ISchemaMigrator Migrator,
    ICompatibilityMetadataWriter Metadata,
    IServerMigrationJournal Journal,
    IApplicationRoleGrants Grants,
    IServerSchemaVerification Verification,
    IDatabaseFingerprintReader State);

/// <summary>Demande d'exécution d'un verbe qui écrit (<c>migrate</c> ou <c>adopt-compatibility</c>).</summary>
public sealed record MigrationRunRequest(
    MigrationRunKind Kind,
    string Operator,
    string? BackupReference,
    string AppRole,
    TimeSpan Wait,
    string ApplicationVersion);

/// <summary>Issue d'une exécution.</summary>
public sealed record MigrationRunResult(MigrationExitCode ExitCode, Guid? RunId, string Message);

/// <summary>
/// Orchestration de <c>migrate</c> et <c>adopt-compatibility</c> (P4-6B, C10 ; §2.3 du plan — ordre
/// <b>normatif</b>) :
/// <code>
///  0  options validées + trace locale OPEN                       échec ⇒ 10, aucun contact serveur
///  1  vérification de sauvegarde (fichiers, preuve)              échec ⇒ 11, aucune écriture
///  2  verrou consultatif                                          échec ⇒ 12, aucune écriture
///  3  sauvegarde ⇔ base courante (P4-9), puis Q-22, rôle          11 / 15 / 10, aucune écriture
///  4  DDL de métadonnée idempotent + GRANT au rôle applicatif     échec ⇒ 13
///  5  NETTOYAGE d'un marqueur de maintenance périmé (sous verrou)
///  6  maintenance_started_at ← now() (ligne d'initialisation si absente)
///  7  journal serveur OPEN (hors transaction de migration)
///  8  Migrate() (sauf adopt)                                      échec ⇒ 13
///  9  vérification post-migration, droits du rôle applicatif      échec ⇒ 14
/// 10a métadonnée : version + minimum CALCULÉ — seulement si 8 et 9 ont réussi
/// 10b journal serveur CLOSE
/// 11  maintenance ← NULL (sauf ligne d'initialisation)            finally, si 6 a été atteinte
/// 12  libération du verrou                                         finally, si 2 a réussi
/// 13  trace locale CLOSE                                           finally, toujours
/// </code>
/// Aucune écriture en base avant le verrou ; ordre de sortie inverse de l'ordre d'entrée.
/// </summary>
public sealed class MigrationRunner
{
    /// <summary><c>lock_timeout</c> de la session qui migre (CX-1).</summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(15);

    private readonly MigrationPorts _ports;
    private readonly IBackupVerification _backupVerification;
    private readonly IMigrationJournal _localTrace;

    public MigrationRunner(MigrationPorts ports, IBackupVerification backupVerification, IMigrationJournal localTrace)
    {
        _ports = ports ?? throw new ArgumentNullException(nameof(ports));
        _backupVerification = backupVerification ?? throw new ArgumentNullException(nameof(backupVerification));
        _localTrace = localTrace ?? throw new ArgumentNullException(nameof(localTrace));
    }

    public async Task<MigrationRunResult> RunAsync(MigrationRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = Guid.NewGuid();
        var verb = request.Kind == MigrationRunKind.Adopt ? "adopt-compatibility" : "migrate";
        _localTrace.Write($"OPEN run {runId} {verb} version {request.ApplicationVersion} " +
                          $"opérateur '{request.Operator}' sauvegarde '{request.BackupReference}' rôle '{request.AppRole}'");

        MigrationRunResult result;
        try
        {
            result = await ExecuteAsync(runId, request, cancellationToken);
        }
        catch (Exception exception)
        {
            result = new MigrationRunResult(MigrationExitCode.MigrationFailed, runId,
                $"Échec inattendu : {ServerCommand.Describe(exception)}");
        }

        // 13 — trace locale de clôture, toujours.
        _localTrace.Write($"CLOSE run {runId} code {(int)result.ExitCode} {result.Message}");
        return result;
    }

    /// <summary>
    /// Diagnostic en lecture seule (<c>status</c>) : Appliquées / Connues, métadonnée, âge de la maintenance,
    /// verdict de la garde pour <paramref name="applicationVersion"/>. Ni verrou, ni écriture.
    /// </summary>
    public async Task<MigrationExitCode> StatusAsync(
        string applicationVersion, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            var known = _ports.Migrator.KnownMigrations;
            var applied = await _ports.Migrator.GetAppliedAsync(cancellationToken);
            var metadata = await _ports.Metadata.ReadAsync(cancellationToken);
            var verdict = await _ports.Metadata.EvaluateAsync(known, applicationVersion, cancellationToken);
            var age = await _ports.Metadata.MaintenanceAgeAsync(cancellationToken);

            output.WriteLine($"Version de l'outil          : {applicationVersion}");
            output.WriteLine($"Migrations appliquées ({applied.Count}) : {string.Join(", ", applied)}");
            output.WriteLine($"Migrations connues ({known.Count})    : {string.Join(", ", known)}");
            output.WriteLine($"En attente                  : {string.Join(", ", known.Except(applied, StringComparer.Ordinal))}");
            output.WriteLine($"Appliquées inconnues        : {string.Join(", ", applied.Except(known, StringComparer.Ordinal))}");
            output.WriteLine($"Métadonnée                  : {metadata.Status}");
            output.WriteLine($"schema_version              : {metadata.SchemaVersion ?? "NULL"}");
            output.WriteLine($"minimum_supported_version   : {metadata.MinimumSupportedVersion ?? "NULL"}");
            output.WriteLine(metadata.MaintenanceStartedAt is { } marker
                ? string.Create(CultureInfo.InvariantCulture,
                    $"Maintenance                 : depuis {marker:o} (âge serveur {age:c}) — seul l'outil, sous verrou, la lève")
                : "Maintenance                 : aucune");
            output.WriteLine($"Verdict (poste {applicationVersion,-8})   : {verdict.State} {verdict.Case} — {verdict.Message}");
            return MigrationExitCode.Success;
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            output.WriteLine("Serveur injoignable ou authentification refusée.");
            return MigrationExitCode.ServerUnreachable;
        }
    }

    private async Task<MigrationRunResult> ExecuteAsync(Guid runId, MigrationRunRequest request, CancellationToken cancellationToken)
    {
        // 0 — options : aucun contact serveur.
        if (string.IsNullOrWhiteSpace(request.Operator))
        {
            return Result(MigrationExitCode.InvalidArguments, runId, "--operator est obligatoire (H13).");
        }

        if (string.IsNullOrWhiteSpace(request.AppRole))
        {
            return Result(MigrationExitCode.InvalidArguments, runId, "--app-role est obligatoire (Q-23).");
        }

        if (!ApplicationVersion.TryParse(request.ApplicationVersion, out _))
        {
            return Result(MigrationExitCode.InvalidArguments, runId,
                $"Version applicative '{request.ApplicationVersion}' invalide : Major.Minor.Patch exigé, sans pré-version.");
        }

        // 1 — sauvegarde vérifiée, AVANT le verrou : un refus n'immobilise jamais la base.
        if (string.IsNullOrWhiteSpace(request.BackupReference))
        {
            return Result(MigrationExitCode.BackupNotVerified, runId, "Référence de sauvegarde absente (--backup-ref, H12).");
        }

        var backup = await _backupVerification.VerifyAsync(request.BackupReference, cancellationToken);
        if (!backup.IsVerified)
        {
            return Result(MigrationExitCode.BackupNotVerified, runId, $"Sauvegarde non vérifiée : {backup.Reason}");
        }

        // 2 — verrou consultatif : seule autorité de sérialisation.
        bool acquired;
        try
        {
            acquired = await _ports.Lock.TryAcquireAsync(request.Wait, cancellationToken);
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return Result(MigrationExitCode.ServerUnreachable, runId, "Serveur injoignable ou authentification refusée.");
        }

        if (!acquired)
        {
            string? holder = null;
            try
            {
                holder = await _ports.Lock.DescribeHolderAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _localTrace.Write($"run {runId} : détenteur du verrou non identifiable ({ServerCommand.Describe(exception)})");
            }

            return Result(MigrationExitCode.LockNotAcquired, runId,
                $"Verrou de migration détenu par une autre exécution ({holder ?? "détenteur non visible"}) : " +
                "aucune écriture effectuée.");
        }

        try
        {
            return await UnderLockAsync(runId, request, backup, cancellationToken);
        }
        finally
        {
            // 12 — libération explicite ; la fin de session n'est qu'un filet.
            try
            {
                await _ports.Lock.ReleaseAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                _localTrace.Write($"run {runId} : libération du verrou en échec ({ServerCommand.Describe(exception)}) — " +
                                  "le serveur le libère en fin de session");
            }
        }
    }

    private async Task<MigrationRunResult> UnderLockAsync(
        Guid runId, MigrationRunRequest request, BackupVerificationResult backup, CancellationToken cancellationToken)
    {
        // 3a — P4-9 : la sauvegarde vérifiée est celle de CETTE base, dans son état ACTUEL. Sous le verrou : aucune
        // autre exécution ne peut migrer entre ce constat et la migration. Lecture seule.
        DatabaseFingerprint live;
        try
        {
            live = await _ports.State.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return Result(MigrationExitCode.ServerUnreachable, runId, "Serveur injoignable ou authentification refusée.");
        }
        catch (Exception exception)
        {
            return Result(MigrationExitCode.BackupNotVerified, runId,
                $"Sauvegarde non rapprochable : état de la base illisible ({ServerCommand.Describe(exception)}).");
        }

        var current = _backupVerification.ConfirmCurrent(backup, live);
        if (!current.IsVerified)
        {
            return Result(MigrationExitCode.BackupNotVerified, runId, $"Sauvegarde non vérifiée : {current.Reason}");
        }

        if (current.Manifest is { } manifest)
        {
            // DP-8 : le journal porte l'identifiant de la sauvegarde VÉRIFIÉE, pas seulement l'emplacement donné.
            request = request with { BackupReference = $"{manifest.BackupId:D} {request.BackupReference}" };
        }

        _localTrace.Write($"run {runId} : sauvegarde rapprochée de la base courante — {request.BackupReference}");

        // 3b — contrôles de cohérence, lecture seule.
        IReadOnlyList<string> appliedBefore;
        ServerCompatibilityMetadata metadata;
        bool roleExists;
        try
        {
            appliedBefore = await _ports.Migrator.GetAppliedAsync(cancellationToken);
            metadata = await _ports.Metadata.ReadAsync(cancellationToken);
            roleExists = await _ports.Grants.RoleExistsAsync(request.AppRole, cancellationToken);
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return Result(MigrationExitCode.ServerUnreachable, runId, "Serveur injoignable ou authentification refusée.");
        }

        var inconsistency = CheckConsistency(request.Kind, appliedBefore.Count > 0, metadata);
        if (inconsistency is not null)
        {
            return Result(MigrationExitCode.MetadataInconsistent, runId, inconsistency);
        }

        if (!roleExists)
        {
            return Result(MigrationExitCode.InvalidArguments, runId,
                "Le rôle --app-role n'existe pas sur le serveur (pg_roles) : aucune écriture effectuée.");
        }

        // 4 — DDL de métadonnée idempotent + GRANT (Q-23) ; aucune ligne insérée ici.
        try
        {
            await _ports.Metadata.EnsureSchemaAsync(cancellationToken);
            await _ports.Grants.GrantAsync(request.AppRole, cancellationToken);
        }
        catch (Exception exception)
        {
            return Result(MigrationExitCode.MigrationFailed, runId,
                $"DDL de métadonnée ou GRANT en échec : {ServerCommand.Describe(exception)}");
        }

        // 5 — NETTOYAGE d'un marqueur périmé : la possession du verrou prouve que son auteur est mort.
        try
        {
            var stale = await _ports.Metadata.ClearStaleMaintenanceAsync(runId, cancellationToken);
            if (stale is { } marker)
            {
                _localTrace.Write(string.Create(CultureInfo.InvariantCulture,
                    $"run {runId} : marqueur de maintenance périmé ({marker:o}) nettoyé sous verrou"));
            }
        }
        catch (Exception exception)
        {
            return Result(MigrationExitCode.MigrationFailed, runId,
                $"Nettoyage du marqueur de maintenance en échec : {ServerCommand.Describe(exception)}");
        }

        // 6 … 11.
        try
        {
            try
            {
                await _ports.Metadata.BeginMaintenanceAsync(runId, cancellationToken);
            }
            catch (Exception exception)
            {
                return Result(MigrationExitCode.MigrationFailed, runId,
                    $"Pose de la maintenance en échec : {ServerCommand.Describe(exception)}");
            }

            var rowInitialized = metadata.Status == ServerMetadataStatus.Present && !metadata.IsInitializationIncomplete;
            return await JournaledAsync(runId, request, appliedBefore, rowInitialized, cancellationToken);
        }
        finally
        {
            // 11 — maintenance ← NULL, sauf ligne en état d'initialisation (§4.3.1).
            try
            {
                await _ports.Metadata.EndMaintenanceAsync(runId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _localTrace.Write($"run {runId} : levée de la maintenance en échec ({ServerCommand.Describe(exception)}) — " +
                                  "le marqueur reste posé jusqu'à la prochaine exécution sous verrou");
            }
        }
    }

    private async Task<MigrationRunResult> JournaledAsync(
        Guid runId, MigrationRunRequest request, IReadOnlyList<string> appliedBefore, bool rowInitialized, CancellationToken cancellationToken)
    {
        // 7 — journal serveur OPEN, hors transaction de migration.
        try
        {
            await _ports.Journal.OpenAsync(new ServerMigrationRun(
                runId, request.ApplicationVersion, appliedBefore, request.Operator, request.Kind,
                request.BackupReference!), cancellationToken);
        }
        catch (Exception exception)
        {
            return Result(MigrationExitCode.MigrationFailed, runId,
                $"Ouverture du journal serveur en échec : {ServerCommand.Describe(exception)}");
        }

        var code = MigrationExitCode.Success;
        string? failure = null;

        // 8 — migration (aucune pour une adoption).
        if (request.Kind == MigrationRunKind.Migrate)
        {
            try
            {
                await _ports.Migrator.MigrateAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                (code, failure) = (MigrationExitCode.MigrationFailed, ServerCommand.Describe(exception));
            }
        }

        // 9 — vérification, dont les droits du rôle applicatif (Q-23).
        if (failure is null)
        {
            try
            {
                var verification = await _ports.Verification.VerifyAsync(request.AppRole, cancellationToken);
                if (!verification.Succeeded)
                {
                    (code, failure) = (MigrationExitCode.VerificationFailed, verification.Failure);
                }
            }
            catch (Exception exception)
            {
                (code, failure) = (MigrationExitCode.VerificationFailed, ServerCommand.Describe(exception));
            }
        }

        // Appliquées réelles, relues : l'outil n'affirme pas l'état, il le constate (R-3).
        IReadOnlyList<string>? appliedAfter = null;
        try
        {
            // Jeton neutre : un processus vivant clôt toujours sa ligne de journal, même annulé.
            appliedAfter = await _ports.Migrator.GetAppliedAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _localTrace.Write($"run {runId} : historique EF illisible après migration ({ServerCommand.Describe(exception)})");
        }

        // 10a — métadonnée, SEULEMENT si 8 et 9 ont réussi ; minimum calculé depuis le journal (H17).
        if (failure is null)
        {
            try
            {
                if (appliedAfter is null)
                {
                    throw new InvalidOperationException("historique EF illisible : métadonnée non avancée");
                }

                var previous = await _ports.Journal.ReadRunsAsync(runId, cancellationToken);
                var current = new MigrationRunRecord(request.ApplicationVersion, request.Kind,
                    MigrationOutcome.Success, appliedBefore, appliedAfter);
                var versions = await _ports.Metadata.AdvanceAsync(runId, [.. previous, current], cancellationToken);
                if (versions is null && !rowInitialized)
                {
                    // §4.3.1 : la ligne resterait en état d'initialisation — jamais un succès.
                    throw new InvalidOperationException(
                        "aucune ancre dans le journal : la ligne de compatibilité reste en état d'initialisation");
                }

                _localTrace.Write(versions is null
                    ? $"run {runId} : aucun schéma produit — métadonnée inchangée"
                    : $"run {runId} : schema_version {versions.SchemaVersion}, minimum_supported_version {versions.MinimumSupportedVersion}");
            }
            catch (Exception exception)
            {
                (code, failure) = (MigrationExitCode.VerificationFailed,
                    $"avancement de la métadonnée en échec : {ServerCommand.Describe(exception)}");
            }
        }

        // 10b — journal serveur CLOSE. Sans Appliquées lisibles, la ligne reste 'open' : preuve de l'échec.
        if (appliedAfter is null)
        {
            _localTrace.Write($"run {runId} : journal serveur laissé 'open' (Appliquées après illisibles)");
        }
        else
        {
            try
            {
                await _ports.Journal.CloseAsync(runId,
                    failure is null ? MigrationOutcome.Success : MigrationOutcome.Failure,
                    failure, appliedAfter, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _localTrace.Write($"run {runId} : clôture du journal serveur en échec ({ServerCommand.Describe(exception)})");
                if (failure is null)
                {
                    (code, failure) = (MigrationExitCode.MigrationFailed,
                        $"clôture du journal serveur en échec : {ServerCommand.Describe(exception)}");
                }
            }
        }

        return failure is null
            ? Result(MigrationExitCode.Success, runId, request.Kind == MigrationRunKind.Adopt
                ? "Compatibilité adoptée."
                : "Migration réussie.")
            : Result(code, runId, $"Exécution en échec : {failure}");
    }

    /// <summary>Contrôle Q-22 (§4.4 du plan), en lecture seule.</summary>
    private static string? CheckConsistency(MigrationRunKind kind, bool historyNonEmpty, ServerCompatibilityMetadata metadata)
    {
        if (metadata.Status == ServerMetadataStatus.Unreadable)
        {
            return "Métadonnée de compatibilité illisible (version non SemVer ou contrainte violée) : intervention requise.";
        }

        var initialized = metadata.Status == ServerMetadataStatus.Present && !metadata.IsInitializationIncomplete;

        if (kind == MigrationRunKind.Adopt)
        {
            if (!historyNonEmpty)
            {
                return "Historique EF vide : rien à adopter — une base vide s'installe avec migrate.";
            }

            return initialized
                ? "Métadonnée déjà initialisée : adopt-compatibility refusé (le minimum se calcule depuis le journal)."
                : null;
        }

        if (!historyNonEmpty)
        {
            return initialized
                ? "Historique EF vide mais métadonnée initialisée : état incohérent, intervention requise."
                : null;
        }

        return metadata.Status is ServerMetadataStatus.Absent or ServerMetadataStatus.NoRow
            ? "Base migrée sans métadonnée de compatibilité : minimum non calculable — adopt-compatibility requis (Q-22)."
            : null;
    }

    private static MigrationRunResult Result(MigrationExitCode code, Guid runId, string message) => new(code, runId, message);
}
