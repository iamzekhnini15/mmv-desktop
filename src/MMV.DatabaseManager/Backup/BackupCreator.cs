using System.Data;
using System.Data.Common;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Backup;

/// <summary>Demande du verbe <c>backup</c>.</summary>
/// <param name="Connection">Rôle de <b>sauvegarde</b> (lecture seule, ADR-PROD-DB-010) sur la base à sauvegarder.</param>
/// <param name="OutputDirectory">Dossier existant, chemin absolu, qui recevra la sauvegarde.</param>
/// <param name="Operator">Référence d'opérateur ou de tâche planifiée, tracée.</param>
/// <param name="PgBinDirectory">Dossier de <c>pg_dump</c> ; <c>null</c> ⇒ <c>PATH</c>.</param>
public sealed record BackupRequest(
    PostgreSqlConnectionSettings Connection,
    string OutputDirectory,
    string Operator,
    string? PgBinDirectory);

/// <summary>
/// Verbe <c>backup</c> (P4-9) : sauvegarde <c>pg_dump</c> (format personnalisé) <b>et</b> manifeste, cohérents
/// entre eux. Une transaction <c>REPEATABLE READ READ ONLY</c> exporte son instantané
/// (<c>pg_export_snapshot()</c>) ; l'état de la base (historique EF, nombre de lignes par table, horloge serveur)
/// est lu <b>dans</b> cet instantané, et <c>pg_dump --snapshot</c> sauvegarde <b>le même</b>. Les écritures
/// concurrentes des postes ne peuvent donc pas désaccorder le manifeste de la sauvegarde.
/// <para>Le fichier est écrit sous un nom provisoire puis renommé ; le manifeste est écrit en dernier. Un échec
/// ne laisse aucun fichier : une sauvegarde partielle n'a jamais de manifeste.</para>
/// </summary>
public sealed class BackupCreator
{
    private readonly IMigrationJournal _trace;

    public BackupCreator(IMigrationJournal trace) => _trace = trace ?? throw new ArgumentNullException(nameof(trace));

    public async Task<AdministrationResult> RunAsync(BackupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runId = Guid.NewGuid();
        _trace.Write($"OPEN backup {runId} version {ApplicationVersion.Current} opérateur '{request.Operator}' " +
                     $"cible {request.Connection}");
        var result = await ExecuteAsync(runId, request, cancellationToken);
        _trace.Write($"CLOSE backup {runId} code {(int)result.ExitCode} {result.Message}");
        return result;
    }

    private async Task<AdministrationResult> ExecuteAsync(Guid backupId, BackupRequest request, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(request.OutputDirectory) || !Directory.Exists(request.OutputDirectory))
        {
            return new(MigrationExitCode.InvalidArguments, "--output-directory doit désigner un dossier existant (chemin absolu).");
        }

        string connectionString;
        try
        {
            connectionString = WithoutPooling(PostgreSqlConnectionSecurity.BuildConnectionString(request.Connection));
        }
        catch (DatabaseConfigurationException exception)
        {
            return new(MigrationExitCode.InvalidArguments, exception.Message);
        }

        var tools = new PostgreSqlClientTools(request.PgBinDirectory);
        string? partial = null;
        try
        {
            await using var connection = PostgreSqlConnectionSecurity.CreateConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var snapshot = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            await ServerCommand.NonQueryAsync(connection, "SET TRANSACTION READ ONLY", cancellationToken);
            await ServerCommand.NonQueryAsync(connection, "SET LOCAL lock_timeout = '60s'", cancellationToken);
            var snapshotId = (string)(await ServerCommand.ScalarAsync(connection, "SELECT pg_export_snapshot()", cancellationToken))!;
            var serverVersion = (string)(await ServerCommand.ScalarAsync(connection, "SHOW server_version", cancellationToken))!;
            var role = (string)(await ServerCommand.ScalarAsync(connection, "SELECT current_user::text", cancellationToken))!;
            var state = await new DatabaseFingerprintReader(connection).ReadAsync(cancellationToken);

            var stem = BackupFiles.Stem(backupId, state.ServerTimeUtc);
            var dumpPath = Path.Combine(request.OutputDirectory, stem + BackupFiles.DumpSuffix);
            partial = dumpPath + BackupFiles.PartialSuffix;

            // Créé ici (propriétaire seul) : pg_dump l'ouvre ensuite en écriture et en conserve les droits.
            await BackupFiles.CreateExclusive(partial).DisposeAsync();
            var dump = await tools.RunAsync("pg_dump",
                ["--format=custom", "--no-password", "--lock-wait-timeout=60000", $"--snapshot={snapshotId}", $"--file={partial}"],
                request.Connection, cancellationToken);
            await snapshot.RollbackAsync(CancellationToken.None);

            if (!dump.Succeeded)
            {
                return new(MigrationExitCode.BackupFailed, $"pg_dump en échec (code {dump.ExitCode}) : {dump.Diagnostics}");
            }

            var (sha256, size) = await BackupFiles.HashFileAsync(partial, cancellationToken);
            if (size == 0)
            {
                return new(MigrationExitCode.BackupFailed, "pg_dump a produit un fichier vide.");
            }

            File.Move(partial, dumpPath);
            partial = dumpPath;

            var manifestPath = Path.Combine(request.OutputDirectory, stem + BackupFiles.ManifestSuffix);
            await BackupFiles.WriteManifestAsync(manifestPath,
                new BackupManifest(backupId, ApplicationVersion.Current, request.Operator, role, serverVersion, state,
                    Path.GetFileName(dumpPath), size, sha256),
                cancellationToken);
            partial = null;

            return new(MigrationExitCode.Success,
                $"Sauvegarde {backupId} écrite ({size} octets, SHA-256 {sha256}). Manifeste : {manifestPath} — " +
                "à vérifier par verify-backup (administrateur) avant toute migration.");
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            return new(MigrationExitCode.ServerUnreachable, "Serveur injoignable ou authentification refusée.");
        }
        catch (PostgreSqlClientToolException exception)
        {
            return new(MigrationExitCode.BackupFailed, exception.Message);
        }
        catch (Exception exception) when (exception is DbException or IOException or UnauthorizedAccessException)
        {
            return new(MigrationExitCode.BackupFailed, $"Sauvegarde en échec : {ServerCommand.Describe(exception)}");
        }
        finally
        {
            if (partial is not null)
            {
                File.Delete(partial);
            }
        }
    }

    /// <summary>Connexions de l'outil sans pool : aucune session ne survit à son usage.</summary>
    internal static string WithoutPooling(string connectionString) =>
        new DbConnectionStringBuilder { ConnectionString = connectionString, ["Pooling"] = false }.ConnectionString;
}
