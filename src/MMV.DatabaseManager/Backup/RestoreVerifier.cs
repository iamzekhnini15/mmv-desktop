using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Backup;

/// <summary>Demande du verbe <c>verify-backup</c>.</summary>
/// <param name="Admin">Administrateur d'installation, connecté à sa base d'administration (<c>--admin-database</c>).</param>
/// <param name="ManifestPath">Chemin absolu du manifeste à vérifier.</param>
/// <param name="Operator">Référence d'opérateur, tracée et inscrite dans la preuve.</param>
/// <param name="PgBinDirectory">Dossier de <c>pg_restore</c> ; <c>null</c> ⇒ <c>PATH</c>.</param>
/// <param name="ScratchDatabase">Base de vérification ; <c>null</c> ⇒ nom dérivé de l'identifiant de sauvegarde.</param>
public sealed record RestoreVerificationRequest(
    PostgreSqlConnectionSettings Admin,
    string ManifestPath,
    string Operator,
    string? PgBinDirectory,
    string? ScratchDatabase);

/// <summary>Demande du verbe <c>restore-backup</c> (reprise après sinistre, §4.6.3 d'ADR-PROD-DB-009).</summary>
/// <param name="Admin">Administrateur d'installation, connecté à sa base d'administration.</param>
/// <param name="ManifestPath">Chemin absolu du manifeste d'une sauvegarde <b>vérifiée</b>.</param>
/// <param name="Operator">Référence d'opérateur, tracée.</param>
/// <param name="PgBinDirectory">Dossier de <c>pg_restore</c> ; <c>null</c> ⇒ <c>PATH</c>.</param>
/// <param name="TargetDatabase">Base <b>neuve</b> à créer ; jamais une base existante.</param>
/// <param name="MigratorRole">Rôle migrateur, propriétaire de la base restaurée (DP-5).</param>
public sealed record BackupRestoreRequest(
    PostgreSqlConnectionSettings Admin,
    string ManifestPath,
    string Operator,
    string? PgBinDirectory,
    string TargetDatabase,
    string MigratorRole);

/// <summary>
/// Restauration réelle d'une sauvegarde (P4-9), par l'<b>administrateur</b> seul (DP-5, ADR-PROD-DB-010) : le
/// migrateur ne reçoit jamais <c>CREATEDB</c>, l'applicatif ne restaure jamais.
/// <para><b><c>verify-backup</c> — contrôle structurel + restauration réelle = sauvegarde vérifiée.</b></para>
/// <list type="number">
///   <item>contrôle structurel, sans serveur : manifeste conforme, taille et SHA-256 du fichier, <c>pg_restore --list</c> ;</item>
///   <item>sérialisation par verrou consultatif ; nettoyage des bases de vérification qu'une exécution interrompue
///   aurait laissées (reconnues à leur commentaire, jamais à leur nom seul) ;</item>
///   <item>restauration réelle (<c>pg_restore --single-transaction --exit-on-error</c>, propriétaires et droits
///   compris) dans une base <b>neuve</b>, fermée à <c>PUBLIC</c> : aucun poste ne peut s'y connecter ;</item>
///   <item>comparaison du contenu restauré au manifeste : historique EF, tables, nombres de lignes ;</item>
///   <item>preuve écrite <b>seulement</b> si tout réussit ; base de vérification <b>toujours</b> supprimée.</item>
/// </list>
/// Sous le verrou, toute vérification commence par retirer la preuve existante : une vérification en échec invalide
/// la précédente ; une exécution refusée faute de verrou n'y touche pas.
/// <para><b><c>restore-backup</c></b> — même chaîne, pour une sauvegarde <b>déjà vérifiée</b>, dans une base
/// <b>neuve</b> propriété du migrateur, fermée à <c>PUBLIC</c> jusqu'à ce que <c>provision</c> rétablisse les droits
/// de connexion. Jamais d'écrasement : une base existante est refusée. Conservée en cas de succès, supprimée si la
/// restauration ou la comparaison échoue.</para>
/// </summary>
public sealed partial class RestoreVerifier
{
    /// <summary>Verrou consultatif des restaurations (« MMVRSTR1 »), distinct de celui des migrations.</summary>
    public const long LockKey = 0x4D4D565253545231;

    /// <summary>Préfixe du commentaire qui marque une base de vérification créée par l'outil.</summary>
    public const string ScratchMarker = "mmv-restore-check:";

    private static readonly string[] ReservedDatabases = ["postgres", "template0", "template1"];

    private readonly IMigrationJournal _trace;

    public RestoreVerifier(IMigrationJournal trace) => _trace = trace ?? throw new ArgumentNullException(nameof(trace));

    /// <summary>Point d'observation des tests : appelé avec la chaîne administrateur de la base restaurée, avant comparaison.</summary>
    public Func<string, Task>? AfterRestore { get; init; }

    public Task<AdministrationResult> RunAsync(RestoreVerificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TracedAsync("verify-backup", request.Operator, request.ManifestPath, request.Admin,
            runId => ExecuteAsync(runId, request.Admin, request.ManifestPath, request.Operator, request.PgBinDirectory,
                verification: true, request.ScratchDatabase, owner: null, cancellationToken));
    }

    public Task<AdministrationResult> RestoreAsync(BackupRestoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TracedAsync("restore-backup", request.Operator, request.ManifestPath, request.Admin,
            runId => ExecuteAsync(runId, request.Admin, request.ManifestPath, request.Operator, request.PgBinDirectory,
                verification: false, request.TargetDatabase, request.MigratorRole, cancellationToken));
    }

    private async Task<AdministrationResult> TracedAsync(
        string verb, string @operator, string manifestPath, PostgreSqlConnectionSettings admin, Func<Guid, Task<AdministrationResult>> body)
    {
        var runId = Guid.NewGuid();
        _trace.Write($"OPEN {verb} {runId} version {ApplicationVersion.Current} opérateur '{@operator}' " +
                     $"manifeste '{manifestPath}' serveur {admin}");
        var result = await body(runId);
        _trace.Write($"CLOSE {verb} {runId} code {(int)result.ExitCode} {result.Message}");
        return result;
    }

    private async Task<AdministrationResult> ExecuteAsync(
        Guid runId, PostgreSqlConnectionSettings adminSettings, string manifestPath, string @operator, string? pgBin,
        bool verification, string? requestedDatabase, string? owner, CancellationToken cancellationToken)
    {
        var failed = verification ? (Func<string, AdministrationResult>)NotVerified : NotRestored;
        if (!Path.IsPathFullyQualified(manifestPath))
        {
            return new(MigrationExitCode.InvalidArguments, "--manifest doit être un chemin absolu.");
        }

        string proofPath;
        try
        {
            proofPath = BackupFiles.ProofPathFor(manifestPath);
        }
        catch (BackupDocumentException exception)
        {
            return new(MigrationExitCode.InvalidArguments, exception.Message);
        }

        // Une reprise après sinistre ne part que d'une sauvegarde VÉRIFIÉE (fichiers et preuve, sans contrôle d'âge).
        if (!verification)
        {
            var verified = await new ProofBackupVerification(BackupPolicy.MaximumAgeBeforeMigration).VerifyAsync(manifestPath, cancellationToken);
            if (!verified.IsVerified)
            {
                return failed($"seule une sauvegarde vérifiée est restaurée — {verified.Reason}");
            }
        }

        // 1 — contrôle structurel, sans serveur. Un échec ici laisse une éventuelle preuve antérieure en place : elle
        // ne vaut plus rien, car migrate recalcule les mêmes empreintes et refuse.
        BackupManifest manifest;
        string manifestSha256;
        string dumpPath;
        try
        {
            (manifest, manifestSha256) = await BackupFiles.ReadManifestAsync(manifestPath, cancellationToken);
            dumpPath = BackupFiles.DumpPathFor(manifestPath, manifest);
            if (!File.Exists(dumpPath))
            {
                return failed("fichier de sauvegarde introuvable à côté du manifeste.");
            }

            var (sha256, size) = await BackupFiles.HashFileAsync(dumpPath, cancellationToken);
            if (size != manifest.DumpSizeBytes || sha256 != manifest.DumpSha256)
            {
                return failed("fichier de sauvegarde altéré ou incomplet (taille ou SHA-256 différente du manifeste).");
            }
        }
        catch (BackupDocumentException exception)
        {
            return failed($"manifeste refusé : {exception.Message}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return failed($"lecture de la sauvegarde impossible : {exception.Message}");
        }

        var database = requestedDatabase ?? $"mmv_restore_check_{manifest.BackupId:N}";
        var refusal = CheckDatabaseName(database, verification, manifest, adminSettings.Database);
        if (refusal is not null)
        {
            return new(MigrationExitCode.InvalidArguments, refusal);
        }

        var tools = new PostgreSqlClientTools(pgBin);
        try
        {
            var list = await tools.RunAsync("pg_restore", ["--list", dumpPath], adminSettings, cancellationToken);
            if (!list.Succeeded)
            {
                return failed($"contrôle structurel en échec — pg_restore --list (code {list.ExitCode}) : {list.Diagnostics}");
            }

            return await OnServerAsync(runId, adminSettings, @operator, manifest, manifestSha256, dumpPath, proofPath,
                verification, database, owner, tools, failed, cancellationToken);
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return new(MigrationExitCode.ServerUnreachable, "Serveur injoignable ou authentification refusée.");
        }
        catch (PostgreSqlClientToolException exception)
        {
            return failed(exception.Message);
        }
        catch (DatabaseConfigurationException exception)
        {
            return new(MigrationExitCode.InvalidArguments, exception.Message);
        }
        catch (Exception exception) when (exception is DbException or IOException or UnauthorizedAccessException)
        {
            return failed(ServerCommand.Describe(exception));
        }
    }

    private async Task<AdministrationResult> OnServerAsync(
        Guid runId, PostgreSqlConnectionSettings adminSettings, string @operator, BackupManifest manifest, string manifestSha256,
        string dumpPath, string proofPath, bool verification, string database, string? owner, PostgreSqlClientTools tools,
        Func<string, AdministrationResult> failed, CancellationToken cancellationToken)
    {
        var verb = verification ? "verify-backup" : "restore-backup";
        await using var admin = PostgreSqlConnectionSecurity.CreateConnection(
            BackupCreator.WithoutPooling(PostgreSqlConnectionSecurity.BuildConnectionString(adminSettings)));
        await admin.OpenAsync(cancellationToken);

        // 2 — une seule restauration à la fois sur ce serveur.
        if (!(bool)(await ServerCommand.ScalarAsync(admin, "SELECT pg_try_advisory_lock(@key)", cancellationToken, ("key", LockKey)))!)
        {
            return new(MigrationExitCode.LockNotAcquired, "Une autre vérification ou restauration de sauvegarde est en cours : rien n'a été modifié.");
        }

        var created = false;
        var keep = false;
        try
        {
            if (verification)
            {
                // Sous le verrou seulement : une exécution refusée (code 12) ne retire jamais la preuve qu'une exécution
                // concurrente vient d'écrire. Dès ici, une vérification en échec ne laisse subsister aucune preuve.
                File.Delete(proofPath);
            }

            try
            {
                await DropLeftoversAsync(admin, runId, verb, cancellationToken);

                if (Convert.ToInt64(await ServerCommand.ScalarAsync(admin,
                        "SELECT count(*) FROM pg_catalog.pg_database WHERE datname = @name", cancellationToken, ("name", database))) > 0)
                {
                    return new(MigrationExitCode.InvalidArguments, $"La base '{database}' existe déjà : elle n'est jamais écrasée.");
                }

                if (owner is not null)
                {
                    var privileged = await ServerCommand.ScalarAsync(admin,
                        "SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls " +
                        "FROM pg_catalog.pg_roles WHERE rolname = @role", cancellationToken, ("role", owner));
                    if (privileged is not bool isPrivileged)
                    {
                        return new(MigrationExitCode.InvalidArguments, $"Le rôle migrateur '{owner}' n'existe pas sur ce serveur.");
                    }

                    if (isPrivileged)
                    {
                        return new(MigrationExitCode.SecurityRefused,
                            $"Le rôle '{owner}' est privilégié : la base restaurée appartient au migrateur, jamais à un rôle privilégié (DP-5).");
                    }

                    await ServerCommand.ExecuteServerFormattedAsync(admin,
                        "SELECT format('CREATE DATABASE %I OWNER %I TEMPLATE template0', @db, @owner)",
                        cancellationToken, ("db", database), ("owner", owner));
                }
                else
                {
                    await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('CREATE DATABASE %I TEMPLATE template0', @db)",
                        cancellationToken, ("db", database));
                }

                created = true;
                if (verification)
                {
                    await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('COMMENT ON DATABASE %I IS %L', @db, @marker)",
                        cancellationToken, ("db", database), ("marker", ScratchMarker + runId.ToString("N")));
                }

                // 3 — base neuve fermée à PUBLIC : aucun poste ne s'y connecte avant décision explicite (provision).
                await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', @db)",
                    cancellationToken, ("db", database));
            }
            catch (Exception exception) when (!ServerCommand.IsServerUnreachable(exception) && exception is DbException)
            {
                return failed($"préparation de la base refusée : {ServerCommand.Describe(exception)} (identifiant administrateur requis)");
            }

            var target = adminSettings with { Database = database };
            var restore = await tools.RunAsync("pg_restore",
                ["--no-password", "--exit-on-error", "--single-transaction", $"--dbname={database}", dumpPath],
                target, cancellationToken);
            if (!restore.Succeeded)
            {
                return failed($"restauration réelle en échec — pg_restore (code {restore.ExitCode}) : {restore.Diagnostics}");
            }

            var targetConnection = BackupCreator.WithoutPooling(PostgreSqlConnectionSecurity.BuildConnectionString(target));
            if (AfterRestore is not null)
            {
                await AfterRestore(targetConnection);
            }

            // 4 — contenu restauré ⇔ manifeste.
            DatabaseFingerprint restored;
            string restorer;
            await using (var connection = PostgreSqlConnectionSecurity.CreateConnection(targetConnection))
            {
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                restorer = (string)(await ServerCommand.ScalarAsync(connection, "SELECT current_user::text", cancellationToken))!;
                restored = await new DatabaseFingerprintReader(connection).ReadAsync(cancellationToken);
            }

            var difference = BackupConsistency.CompareContent(manifest, restored);
            if (difference is not null)
            {
                return failed($"contenu restauré différent du manifeste : {difference}");
            }

            var summary = $"{restored.Tables.Count} tables, {restored.Tables.Sum(t => t.Rows)} lignes, " +
                          $"{restored.AppliedMigrations.Count} migration(s)";
            if (!verification)
            {
                keep = true;
                return new(MigrationExitCode.Success,
                    $"Sauvegarde {manifest.BackupId} restaurée dans la base neuve '{database}' ({summary}), fermée aux postes. " +
                    "Suite : provision --database " + database + " (droits de connexion), puis nouvelle sauvegarde vérifiée " +
                    "avant toute migration, puis configure-workstation des postes vers cette base.");
            }

            // 5 — preuve.
            await BackupFiles.WriteProofAsync(proofPath,
                new BackupProof(manifest.BackupId, manifestSha256, manifest.DumpSha256, restored.ServerTimeUtc, restorer,
                    @operator, ApplicationVersion.Current, restored.SystemIdentifier, database,
                    restored.Tables.Count, restored.Tables.Sum(t => t.Rows)),
                cancellationToken);

            return new(MigrationExitCode.Success,
                $"Sauvegarde {manifest.BackupId} vérifiée par restauration réelle ({summary}). Preuve : {proofPath}");
        }
        finally
        {
            if (created && !keep)
            {
                await DropAsync(admin, runId, verb, database);
            }

            try
            {
                await ServerCommand.ScalarAsync(admin, "SELECT pg_advisory_unlock(@key)", CancellationToken.None, ("key", LockKey));
            }
            catch (Exception exception)
            {
                _trace.Write($"{verb} {runId} : libération du verrou en échec ({ServerCommand.Describe(exception)}) — " +
                             "le serveur le libère en fin de session");
            }
        }
    }

    /// <summary>Bases de vérification laissées par une exécution interrompue : reconnues au marqueur, sous le verrou.</summary>
    private async Task DropLeftoversAsync(DbConnection admin, Guid runId, string verb, CancellationToken cancellationToken)
    {
        var leftovers = new List<string>();
        await using (var command = ServerCommand.Create(admin,
                         "SELECT datname::text FROM pg_catalog.pg_database " +
                         "WHERE starts_with(coalesce(pg_catalog.shobj_description(oid, 'pg_database'), ''), @marker)",
                         ("marker", ScratchMarker)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                leftovers.Add(reader.GetString(0));
            }
        }

        foreach (var name in leftovers)
        {
            await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('DROP DATABASE %I WITH (FORCE)', @db)",
                cancellationToken, ("db", name));
            _trace.Write($"{verb} {runId} : base de vérification résiduelle '{name}' supprimée");
        }
    }

    private async Task DropAsync(DbConnection admin, Guid runId, string verb, string database)
    {
        try
        {
            await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', @db)",
                CancellationToken.None, ("db", database));
        }
        catch (Exception exception)
        {
            _trace.Write($"{verb} {runId} : suppression de '{database}' en échec ({ServerCommand.Describe(exception)}) — " +
                         "à supprimer par l'administrateur (une base de vérification l'est par la prochaine vérification)");
        }
    }

    private static string? CheckDatabaseName(string database, bool verification, BackupManifest manifest, string adminDatabase)
    {
        var option = verification ? "--scratch-database" : "--target-database";
        if (!DatabaseNamePattern().IsMatch(database))
        {
            return $"{option} : minuscules, chiffres et « _ » seulement, 63 octets au plus, sans chiffre en tête.";
        }

        // Une base de vérification n'est jamais la base sauvegardée ; une cible de reprise peut en reprendre le nom
        // si elle a disparu — l'existence est refusée plus loin, sous le verrou.
        if ((verification && string.Equals(database, manifest.State.Database, StringComparison.Ordinal))
            || string.Equals(database, adminDatabase, StringComparison.Ordinal)
            || ReservedDatabases.Contains(database, StringComparer.Ordinal))
        {
            return $"{option} '{database}' refusée : jamais la base sauvegardée, la base d'administration ni une base système.";
        }

        return null;
    }

    private static AdministrationResult NotVerified(string message) =>
        new(MigrationExitCode.RestoreVerificationFailed, $"Sauvegarde NON vérifiée : {message}");

    private static AdministrationResult NotRestored(string message) =>
        new(MigrationExitCode.RestoreVerificationFailed, $"Restauration NON effectuée : {message}");

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex DatabaseNamePattern();
}
