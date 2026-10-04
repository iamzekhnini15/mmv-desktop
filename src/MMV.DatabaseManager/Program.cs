using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.CommandLine;
using MMV.DatabaseManager.Journal;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager;

/// <summary>
/// Point d'entrée de <c>MMV.DatabaseManager</c> (P4-6B, C3) : arguments → composition → runner → code de
/// sortie. Aucune logique de migration ici. Racine de composition : la <b>seule</b> vérification de
/// sauvegarde construite est <see cref="RefusingBackupVerification"/> (H12) ; P4-9 la remplacera ici, par un
/// commit revu.
/// </summary>
public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, null, Console.Out, Console.Error);

    /// <param name="args">Arguments de ligne de commande.</param>
    /// <param name="environment">Variables injectables pour les tests ; <c>null</c> ⇒ environnement réel.</param>
    /// <param name="output">Sortie standard.</param>
    /// <param name="error">Sortie d'erreur.</param>
    /// <param name="traceFilePath">Trace locale ; <c>null</c> ⇒ dossier de données de l'utilisateur (DI-9).</param>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environment,
        TextWriter output,
        TextWriter error,
        string? traceFilePath = null)
    {
        var parsed = MigrationToolOptions.Parse(args);
        if (!parsed.IsValid)
        {
            error.WriteLine(parsed.Error);
            error.WriteLine(MigrationToolOptions.Usage);
            return (int)MigrationExitCode.InvalidArguments;
        }

        var options = parsed.Options!;
        var connectionString = environment is not null
            ? environment.GetValueOrDefault(MigrationToolOptions.ConnectionStringVariableName)
            : Environment.GetEnvironmentVariable(MigrationToolOptions.ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine($"{MigrationToolOptions.ConnectionStringVariableName} est absente ou vide : " +
                            "chaîne de connexion du rôle migrateur requise (jamais celle d'un poste).");
            return (int)MigrationExitCode.InvalidArguments;
        }

        try
        {
            return await ExecuteAsync(options, connectionString.Trim(), output, error, traceFilePath);
        }
        catch (ArgumentException)
        {
            // Chaîne de connexion mal formée : jamais restituée (elle porte un secret).
            error.WriteLine($"{MigrationToolOptions.ConnectionStringVariableName} est mal formée.");
            return (int)MigrationExitCode.InvalidArguments;
        }
        catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
        {
            error.WriteLine("Serveur injoignable ou authentification refusée.");
            return (int)MigrationExitCode.ServerUnreachable;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Échec inattendu : {ServerCommand.Describe(exception)}");
            return (int)MigrationExitCode.MigrationFailed;
        }
    }

    private static async Task<int> ExecuteAsync(
        MigrationToolOptions options, string connectionString, TextWriter output, TextWriter error, string? traceFilePath)
    {
        var localTrace = new MigrationJournal(traceFilePath ?? DefaultTraceFilePath());
        await using var session = PostgreSqlMigrationSession.Create(
            builder => DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
            {
                Provider = DatabaseProvider.PostgreSql,
                ConnectionString = connectionString
            }),
            MigrationRunner.DefaultLockTimeout);
        var runner = new MigrationRunner(session.Ports, new RefusingBackupVerification(), localTrace);

        if (options.Verb == MigrationVerb.Status)
        {
            try
            {
                await session.OpenForStatusAsync();
            }
            catch (Exception exception) when (ServerCommand.IsServerUnreachable(exception))
            {
                error.WriteLine("Serveur injoignable ou authentification refusée.");
                return (int)MigrationExitCode.ServerUnreachable;
            }

            return (int)await runner.StatusAsync(ApplicationVersion.Current, output);
        }

        var result = await runner.RunAsync(new MigrationRunRequest(
            options.Verb == MigrationVerb.AdoptCompatibility ? MigrationRunKind.Adopt : MigrationRunKind.Migrate,
            options.Operator!,
            options.BackupReference,
            options.AppRole!,
            options.Wait,
            ApplicationVersion.Current));

        (result.ExitCode == MigrationExitCode.Success ? output : error)
            .WriteLine($"[run {result.RunId}] code {(int)result.ExitCode} — {result.Message}");
        return (int)result.ExitCode;
    }

    /// <summary>
    /// Trace locale dans le dossier de données de l'utilisateur, jamais sous le dossier d'installation que la
    /// mise à jour remplace (DI-9). Emplacement définitif à arbitrer avec P8.
    /// </summary>
    private static string DefaultTraceFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        SqliteDatabasePathResolver.ApplicationFolderName,
        "DatabaseManager",
        "migration-trace.log");
}
