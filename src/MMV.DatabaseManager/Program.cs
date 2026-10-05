using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.CommandLine;
using MMV.DatabaseManager.Import;
using MMV.DatabaseManager.Journal;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager;

/// <summary>
/// Point d'entrée de <c>MMV.DatabaseManager</c> (P4-6B, C3) : arguments → composition → runner → code de
/// sortie. Aucune logique de migration ici. Racine de composition : la <b>seule</b> vérification de
/// sauvegarde construite est <see cref="ProofBackupVerification"/> (H12, DP-10, P4-9) — aucune option, variable
/// ni configuration ne la remplace.
/// </summary>
public static class Program
{
    public static Task<int> Main(string[] args) =>
        RunAsync(args, null, Console.Out, Console.Error, null, Console.In, !Console.IsInputRedirected);

    /// <param name="args">Arguments de ligne de commande.</param>
    /// <param name="environment">Variables injectables pour les tests ; <c>null</c> ⇒ environnement réel.</param>
    /// <param name="output">Sortie standard.</param>
    /// <param name="error">Sortie d'erreur.</param>
    /// <param name="traceFilePath">Trace locale ; <c>null</c> ⇒ dossier de données de l'utilisateur (DI-9).</param>
    /// <param name="input">Entrée des secrets des verbes P4-8 ; <c>null</c> ⇒ aucune (secret manquant ⇒ code 10).</param>
    /// <param name="interactiveInput">Saisie masquée en console (verbes P4-8).</param>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environment,
        TextWriter output,
        TextWriter error,
        string? traceFilePath = null,
        TextReader? input = null,
        bool interactiveInput = false)
    {
        // P4-8 (ADR-PROD-DB-010) : verbes d'administration — provisioning, rotation, premier administrateur,
        // configuration de poste ; P4-9 : sauvegarde et vérification par restauration. Aucun n'applique de migration.
        if (AdministrationOptions.IsAdministrationVerb(args))
        {
            return await AdministrationCommands.RunAsync(args, MigratorConnectionString(environment), output, error,
                new MigrationJournal(traceFilePath ?? DefaultTraceFilePath()),
                new SecretInput(input ?? TextReader.Null, output, interactiveInput));
        }

        // P4-7 : import SQLite → PostgreSQL, sous le rôle migrateur, gardé par la sauvegarde vérifiée et le verrou.
        ImportOptions? import = null;
        MigrationToolOptions? options = null;
        if (ImportOptions.IsImportVerb(args))
        {
            var parsedImport = ImportOptions.Parse(args);
            if (!parsedImport.IsValid)
            {
                error.WriteLine(parsedImport.Error);
                error.WriteLine(ImportOptions.Usage);
                return (int)MigrationExitCode.InvalidArguments;
            }

            import = parsedImport.Options!;
        }
        else
        {
            var parsed = MigrationToolOptions.Parse(args);
            if (!parsed.IsValid)
            {
                error.WriteLine(parsed.Error);
                error.WriteLine(MigrationToolOptions.Usage);
                return (int)MigrationExitCode.InvalidArguments;
            }

            options = parsed.Options!;
        }

        var connectionString = MigratorConnectionString(environment);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error.WriteLine($"{MigrationToolOptions.ConnectionStringVariableName} est absente ou vide : " +
                            "chaîne de connexion du rôle migrateur requise (jamais celle d'un poste).");
            return (int)MigrationExitCode.InvalidArguments;
        }

        try
        {
            // P4-8 (D-14) : TLS VerifyFull et SCRAM-SHA-256 imposés, réglage affaibli refusé — sans contact serveur.
            var hardened = PostgreSqlConnectionSecurity.Harden(connectionString.Trim());
            return import is not null
                ? await ImportAsync(import, hardened, output, error, traceFilePath)
                : await ExecuteAsync(options!, hardened, output, error, traceFilePath);
        }
        catch (ArgumentException)
        {
            // Chaîne de connexion mal formée : jamais restituée (elle porte un secret).
            error.WriteLine($"{MigrationToolOptions.ConnectionStringVariableName} est mal formée.");
            return (int)MigrationExitCode.InvalidArguments;
        }
        catch (DatabaseConfigurationException exception)
        {
            error.WriteLine(exception.Message);
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
        var runner = new MigrationRunner(session.Ports, BackupVerification(), localTrace);

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

    private static async Task<int> ImportAsync(
        ImportOptions options, string connectionString, TextWriter output, TextWriter error, string? traceFilePath)
    {
        await using var session = PostgreSqlMigrationSession.Create(
            builder => DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions
            {
                Provider = DatabaseProvider.PostgreSql,
                ConnectionString = connectionString
            }),
            MigrationRunner.DefaultLockTimeout);
        var runner = new SqliteImportRunner(
            session.Ports,
            session.Connection,
            ImportPlan.From(session.DesignTimeModel),
            BackupVerification(),
            new MigrationJournal(traceFilePath ?? DefaultTraceFilePath()),
            async cancellationToken =>
            {
                var fresh = PostgreSqlConnectionSecurity.CreateConnection(connectionString);
                await fresh.OpenAsync(cancellationToken);
                return fresh;
            });

        var result = await runner.RunAsync(new SqliteImportRequest(
            options.Source, options.SourceTimeZone, options.Operator, options.BackupReference, options.Report,
            options.DryRun, options.Wait, ApplicationVersion.Current));

        (result.ExitCode == MigrationExitCode.Success ? output : error)
            .WriteLine($"[import {result.RunId}] code {(int)result.ExitCode} — {result.Message}");
        output.WriteLine($"Rapport : {Path.GetFullPath(options.Report)}");
        return (int)result.ExitCode;
    }

    /// <summary>
    /// <b>Seul</b> point de construction de la vérification de sauvegarde, commun à <c>migrate</c> et à
    /// <c>import-sqlite</c> (DP-10, P4-9) : aucune option ni configuration ne la remplace ni ne relâche son âge.
    /// </summary>
    private static IBackupVerification BackupVerification() => new ProofBackupVerification(BackupPolicy.MaximumAgeBeforeMigration);

    /// <summary>Seule variable lue par l'outil : la chaîne du rôle migrateur (jamais celle d'un poste).</summary>
    private static string? MigratorConnectionString(IReadOnlyDictionary<string, string?>? environment) =>
        environment is not null
            ? environment.GetValueOrDefault(MigrationToolOptions.ConnectionStringVariableName)
            : Environment.GetEnvironmentVariable(MigrationToolOptions.ConnectionStringVariableName);

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
