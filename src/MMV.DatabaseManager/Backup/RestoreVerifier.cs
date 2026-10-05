using System.Data;
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

/// <summary>
/// Verbe <c>verify-backup</c> (P4-9) — <b>contrôle structurel + restauration réelle = sauvegarde vérifiée</b>.
/// Exécuté par l'<b>administrateur</b>, seul détenteur de la restauration (DP-5, ADR-PROD-DB-010) : le migrateur ne
/// reçoit jamais <c>CREATEDB</c> pour cela.
/// <list type="number">
///   <item>contrôle structurel, sans serveur : manifeste conforme, taille et SHA-256 du fichier, <c>pg_restore --list</c> ;</item>
///   <item>sérialisation par verrou consultatif ; nettoyage des bases de vérification qu'une exécution interrompue
///   aurait laissées (reconnues à leur commentaire, jamais à leur nom seul) ;</item>
///   <item>restauration réelle (<c>pg_restore --single-transaction --exit-on-error</c>, propriétaires et droits
///   compris) dans une base <b>neuve</b>, fermée à <c>PUBLIC</c> : aucun poste ne peut s'y connecter ;</item>
///   <item>comparaison du contenu restauré au manifeste : historique EF, tables, nombres de lignes ;</item>
///   <item>preuve écrite <b>seulement</b> si tout réussit ; base de vérification <b>toujours</b> supprimée.</item>
/// </list>
/// Sous le verrou, toute exécution commence par retirer la preuve existante : une vérification en échec invalide la
/// précédente ; une exécution refusée faute de verrou n'y touche pas.
/// </summary>
public sealed partial class RestoreVerifier
{
    /// <summary>Verrou consultatif des vérifications (« MMVRSTR1 »), distinct de celui des migrations.</summary>
    public const long LockKey = 0x4D4D565253545231;

    /// <summary>Préfixe du commentaire qui marque une base de vérification créée par l'outil.</summary>
    public const string ScratchMarker = "mmv-restore-check:";

    private static readonly string[] ReservedDatabases = ["postgres", "template0", "template1"];

    private readonly IMigrationJournal _trace;

    public RestoreVerifier(IMigrationJournal trace) => _trace = trace ?? throw new ArgumentNullException(nameof(trace));

    /// <summary>Point d'observation des tests : appelé avec la chaîne administrateur de la base restaurée, avant comparaison.</summary>
    public Func<string, Task>? AfterRestore { get; init; }

    public async Task<AdministrationResult> RunAsync(RestoreVerificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = Guid.NewGuid();
        _trace.Write($"OPEN verify-backup {runId} version {ApplicationVersion.Current} opérateur '{request.Operator}' " +
                     $"manifeste '{request.ManifestPath}' serveur {request.Admin}");
        var result = await ExecuteAsync(runId, request, cancellationToken);
        _trace.Write($"CLOSE verify-backup {runId} code {(int)result.ExitCode} {result.Message}");
        return result;
    }

    private async Task<AdministrationResult> ExecuteAsync(Guid runId, RestoreVerificationRequest request, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(request.ManifestPath))
        {
            return new(MigrationExitCode.InvalidArguments, "--manifest doit être un chemin absolu.");
        }

        string proofPath;
        try
        {
            proofPath = BackupFiles.ProofPathFor(request.ManifestPath);
        }
        catch (BackupDocumentException exception)
        {
            return new(MigrationExitCode.InvalidArguments, exception.Message);
        }

        // 1 — contrôle structurel, sans serveur. Un échec ici laisse une éventuelle preuve antérieure en place : elle
        // ne vaut plus rien, car migrate recalcule les mêmes empreintes et refuse.
        BackupManifest manifest;
        string manifestSha256;
        string dumpPath;
        try
        {
            (manifest, manifestSha256) = await BackupFiles.ReadManifestAsync(request.ManifestPath, cancellationToken);
            dumpPath = BackupFiles.DumpPathFor(request.ManifestPath, manifest);
            if (!File.Exists(dumpPath))
            {
                return Failed("fichier de sauvegarde introuvable à côté du manifeste.");
            }

            var (sha256, size) = await BackupFiles.HashFileAsync(dumpPath, cancellationToken);
            if (size != manifest.DumpSizeBytes || sha256 != manifest.DumpSha256)
            {
                return Failed("fichier de sauvegarde altéré ou incomplet (taille ou SHA-256 différente du manifeste).");
            }
        }
        catch (BackupDocumentException exception)
        {
            return Failed($"manifeste refusé : {exception.Message}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed($"lecture de la sauvegarde impossible : {exception.Message}");
        }

        var scratch = request.ScratchDatabase ?? $"mmv_restore_check_{manifest.BackupId:N}";
        var refusal = CheckScratchName(scratch, manifest, request.Admin.Database);
        if (refusal is not null)
        {
            return new(MigrationExitCode.InvalidArguments, refusal);
        }

        var tools = new PostgreSqlClientTools(request.PgBinDirectory);
        try
        {
            var list = await tools.RunAsync("pg_restore", ["--list", dumpPath], request.Admin, cancellationToken);
            if (!list.Succeeded)
            {
                return Failed($"contrôle structurel en échec — pg_restore --list (code {list.ExitCode}) : {list.Diagnostics}");
            }

            return await OnServerAsync(runId, request, manifest, manifestSha256, dumpPath, proofPath, scratch, tools, cancellationToken);
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return new(MigrationExitCode.ServerUnreachable, "Serveur injoignable ou authentification refusée.");
        }
        catch (PostgreSqlClientToolException exception)
        {
            return Failed(exception.Message);
        }
        catch (DatabaseConfigurationException exception)
        {
            return new(MigrationExitCode.InvalidArguments, exception.Message);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or IOException or UnauthorizedAccessException)
        {
            return Failed(ServerCommand.Describe(exception));
        }
    }

    private async Task<AdministrationResult> OnServerAsync(
        Guid runId, RestoreVerificationRequest request, BackupManifest manifest, string manifestSha256, string dumpPath,
        string proofPath, string scratch, PostgreSqlClientTools tools, CancellationToken cancellationToken)
    {
        await using var admin = PostgreSqlConnectionSecurity.CreateConnection(
            BackupCreator.WithoutPooling(PostgreSqlConnectionSecurity.BuildConnectionString(request.Admin)));
        await admin.OpenAsync(cancellationToken);

        // 2 — une seule vérification à la fois sur ce serveur.
        if (!(bool)(await ServerCommand.ScalarAsync(admin, "SELECT pg_try_advisory_lock(@key)", cancellationToken, ("key", LockKey)))!)
        {
            return new(MigrationExitCode.LockNotAcquired, "Une autre vérification de sauvegarde est en cours : rien n'a été modifié.");
        }

        var created = false;
        try
        {
            // Sous le verrou seulement : une exécution refusée (code 12) ne retire jamais la preuve qu'une exécution
            // concurrente vient d'écrire. Dès ici, une vérification en échec ne laisse subsister aucune preuve.
            File.Delete(proofPath);

            try
            {
                await DropLeftoversAsync(admin, runId, cancellationToken);

                if (Convert.ToInt64(await ServerCommand.ScalarAsync(admin,
                        "SELECT count(*) FROM pg_catalog.pg_database WHERE datname = @name", cancellationToken, ("name", scratch))) > 0)
                {
                    return new(MigrationExitCode.InvalidArguments,
                        $"La base de vérification '{scratch}' existe déjà : elle n'est jamais écrasée.");
                }

                // 3 — base neuve, marquée, fermée à PUBLIC.
                await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('CREATE DATABASE %I TEMPLATE template0', @db)",
                    cancellationToken, ("db", scratch));
                created = true;
                await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('COMMENT ON DATABASE %I IS %L', @db, @marker)",
                    cancellationToken, ("db", scratch), ("marker", ScratchMarker + runId.ToString("N")));
                await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', @db)",
                    cancellationToken, ("db", scratch));
            }
            catch (Exception exception) when (!ServerCommand.IsServerUnreachable(exception) && exception is System.Data.Common.DbException)
            {
                return Failed($"préparation de la base de vérification refusée : {ServerCommand.Describe(exception)} " +
                              "(identifiant administrateur requis)");
            }

            var scratchTarget = request.Admin with { Database = scratch };
            var restore = await tools.RunAsync("pg_restore",
                ["--no-password", "--exit-on-error", "--single-transaction", $"--dbname={scratch}", dumpPath],
                scratchTarget, cancellationToken);
            if (!restore.Succeeded)
            {
                return Failed($"restauration réelle en échec — pg_restore (code {restore.ExitCode}) : {restore.Diagnostics}");
            }

            var scratchConnection = BackupCreator.WithoutPooling(PostgreSqlConnectionSecurity.BuildConnectionString(scratchTarget));
            if (AfterRestore is not null)
            {
                await AfterRestore(scratchConnection);
            }

            // 4 — contenu restauré ⇔ manifeste.
            DatabaseFingerprint restored;
            string verifier;
            await using (var connection = PostgreSqlConnectionSecurity.CreateConnection(scratchConnection))
            {
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
                verifier = (string)(await ServerCommand.ScalarAsync(connection, "SELECT current_user::text", cancellationToken))!;
                restored = await new DatabaseFingerprintReader(connection).ReadAsync(cancellationToken);
            }

            var difference = BackupConsistency.CompareContent(manifest, restored);
            if (difference is not null)
            {
                return Failed($"contenu restauré différent du manifeste : {difference}");
            }

            // 5 — preuve.
            await BackupFiles.WriteProofAsync(proofPath,
                new BackupProof(manifest.BackupId, manifestSha256, manifest.DumpSha256, restored.ServerTimeUtc, verifier,
                    request.Operator, ApplicationVersion.Current, restored.SystemIdentifier, scratch,
                    restored.Tables.Count, restored.Tables.Sum(t => t.Rows)),
                cancellationToken);

            return new(MigrationExitCode.Success,
                $"Sauvegarde {manifest.BackupId} vérifiée par restauration réelle ({restored.Tables.Count} tables, " +
                $"{restored.Tables.Sum(t => t.Rows)} lignes, {restored.AppliedMigrations.Count} migration(s)). Preuve : {proofPath}");
        }
        finally
        {
            if (created)
            {
                await DropScratchAsync(admin, runId, scratch);
            }

            try
            {
                await ServerCommand.ScalarAsync(admin, "SELECT pg_advisory_unlock(@key)", CancellationToken.None, ("key", LockKey));
            }
            catch (Exception exception)
            {
                _trace.Write($"verify-backup {runId} : libération du verrou en échec ({ServerCommand.Describe(exception)}) — " +
                             "le serveur le libère en fin de session");
            }
        }
    }

    /// <summary>Bases de vérification laissées par une exécution interrompue : reconnues au marqueur, sous le verrou.</summary>
    private async Task DropLeftoversAsync(System.Data.Common.DbConnection admin, Guid runId, CancellationToken cancellationToken)
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
            _trace.Write($"verify-backup {runId} : base de vérification résiduelle '{name}' supprimée");
        }
    }

    private async Task DropScratchAsync(System.Data.Common.DbConnection admin, Guid runId, string scratch)
    {
        try
        {
            await ServerCommand.ExecuteServerFormattedAsync(admin, "SELECT format('DROP DATABASE IF EXISTS %I WITH (FORCE)', @db)",
                CancellationToken.None, ("db", scratch));
        }
        catch (Exception exception)
        {
            _trace.Write($"verify-backup {runId} : suppression de '{scratch}' en échec ({ServerCommand.Describe(exception)}) — " +
                         "elle sera supprimée par la prochaine vérification");
        }
    }

    private static string? CheckScratchName(string scratch, BackupManifest manifest, string adminDatabase)
    {
        if (!ScratchNamePattern().IsMatch(scratch))
        {
            return "--scratch-database : minuscules, chiffres et « _ » seulement, 63 octets au plus, sans chiffre en tête.";
        }

        if (string.Equals(scratch, manifest.State.Database, StringComparison.Ordinal)
            || string.Equals(scratch, adminDatabase, StringComparison.Ordinal)
            || ReservedDatabases.Contains(scratch, StringComparer.Ordinal))
        {
            return $"--scratch-database '{scratch}' refusée : jamais la base sauvegardée, la base d'administration ni une base système.";
        }

        return null;
    }

    private static AdministrationResult Failed(string message) =>
        new(MigrationExitCode.RestoreVerificationFailed, $"Sauvegarde NON vérifiée : {message}");

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex ScratchNamePattern();
}
