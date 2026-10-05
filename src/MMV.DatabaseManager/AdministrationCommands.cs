using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.CommandLine;
using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager;

/// <summary>
/// Verbes d'administration P4-8 (ADR-PROD-DB-010) et de sauvegarde P4-9 : arguments → secrets (entrée standard) →
/// composant → code de sortie. Aucune logique ici. Ces verbes n'appliquent <b>aucune</b> migration : <c>migrate</c>
/// reste le seul chemin de DDL de schéma (DP-1). <c>verify-backup</c> produit la preuve que <c>migrate</c> exige ;
/// il ne la contourne pas.
/// </summary>
internal static class AdministrationCommands
{
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        string? migratorConnectionString,
        TextWriter output,
        TextWriter error,
        IMigrationJournal trace,
        SecretInput secrets)
    {
        var parsed = AdministrationOptions.Parse(args);
        if (!parsed.IsValid)
        {
            error.WriteLine(parsed.Error);
            error.WriteLine(AdministrationOptions.Usage);
            return (int)MigrationExitCode.InvalidArguments;
        }

        var options = parsed.Options!;
        AdministrationResult result;
        switch (options.Verb)
        {
            case AdministrationVerb.Provision:
            {
                if (!TryRead(secrets, error, out var values, "Secret de l'administrateur PostgreSQL",
                        "Secret du rôle migrateur", "Secret du rôle applicatif", "Secret du rôle de sauvegarde"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new PostgreSqlProvisioner(trace).RunAsync(new ProvisioningRequest
                {
                    Host = options.Require("--host"),
                    Port = options.Port,
                    RootCertificatePath = options.Get("--root-certificate"),
                    AdminUser = options.Require("--admin-user"),
                    AdminPassword = values[0],
                    AdminDatabase = options.AdminDatabase,
                    Database = options.Require("--database"),
                    MigratorRole = options.Require("--migrator-role"),
                    MigratorPassword = values[1],
                    AppRole = options.Require("--app-role"),
                    AppPassword = values[2],
                    BackupRole = options.Require("--backup-role"),
                    BackupPassword = values[3],
                    Operator = options.Require("--operator")
                });
                break;
            }

            case AdministrationVerb.RotateRolePassword:
            {
                if (!TryRead(secrets, error, out var values, "Secret de l'administrateur PostgreSQL", "Nouveau secret du rôle"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new RolePasswordRotation(trace).RunAsync(new RoleRotationRequest
                {
                    Host = options.Require("--host"),
                    Port = options.Port,
                    RootCertificatePath = options.Get("--root-certificate"),
                    AdminUser = options.Require("--admin-user"),
                    AdminPassword = values[0],
                    AdminDatabase = options.AdminDatabase,
                    Database = options.Require("--database"),
                    Role = options.Require("--role"),
                    NewPassword = values[1],
                    Operator = options.Require("--operator")
                });
                break;
            }

            case AdministrationVerb.BootstrapAdmin:
            {
                if (string.IsNullOrWhiteSpace(migratorConnectionString))
                {
                    error.WriteLine($"{MigrationToolOptions.ConnectionStringVariableName} est absente ou vide : " +
                                    "chaîne de connexion du rôle migrateur requise (jamais celle d'un poste).");
                    return (int)MigrationExitCode.InvalidArguments;
                }

                string hardened;
                try
                {
                    hardened = PostgreSqlConnectionSecurity.Harden(migratorConnectionString.Trim());
                }
                catch (ArgumentException)
                {
                    error.WriteLine($"{MigrationToolOptions.ConnectionStringVariableName} est mal formée.");
                    return (int)MigrationExitCode.InvalidArguments;
                }
                catch (DatabaseConfigurationException exception)
                {
                    error.WriteLine(exception.Message);
                    return (int)MigrationExitCode.InvalidArguments;
                }

                if (!TryRead(secrets, error, out var values, "Mot de passe initial de l'administrateur", "Confirmation"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new BootstrapAdministrator(trace).RunAsync(
                    hardened, options.Require("--username"), values[0], values[1], options.Require("--operator"));
                break;
            }

            case AdministrationVerb.ConfigureWorkstation:
            {
                if (!OperatingSystem.IsWindows())
                {
                    error.WriteLine("configure-workstation s'exécute sur le poste Windows, sous l'utilisateur qui lancera MMV (DPAPI).");
                    return (int)MigrationExitCode.InvalidArguments;
                }

                if (!TryRead(secrets, error, out var values, "Secret du rôle applicatif"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new WorkstationConfigurator(WorkstationDatabaseSettingsFile.CreateDefault(), trace).RunAsync(
                    new PostgreSqlConnectionSettings(options.Require("--host"), options.Port, options.Require("--database"),
                        options.Require("--username"), values[0], options.Get("--root-certificate")));
                break;
            }

            case AdministrationVerb.Backup:
            {
                var connection = BackupConnection(options, secrets, error);
                if (connection is null)
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new BackupCreator(trace).RunAsync(new BackupRequest(
                    connection,
                    options.Require("--output-directory"),
                    options.Require("--operator"),
                    options.Get("--pg-bin")));
                break;
            }

            case AdministrationVerb.ConfigureBackup:
            {
                if (!OperatingSystem.IsWindows())
                {
                    error.WriteLine("configure-backup s'exécute sur le serveur Windows, sous le compte de la tâche planifiée (DPAPI).");
                    return (int)MigrationExitCode.InvalidArguments;
                }

                if (!TryRead(secrets, error, out var values, "Secret du rôle de sauvegarde"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new WorkstationConfigurator(BackupConnectionFile.CreateDefault(), trace, "configure-backup").RunAsync(
                    new PostgreSqlConnectionSettings(options.Require("--host"), options.Port, options.Require("--database"),
                        options.Require("--username"), values[0], options.Get("--root-certificate")));
                break;
            }

            case AdministrationVerb.PruneBackups:
                result = await new BackupPruner(trace).RunAsync(options.Require("--directory"), options.Require("--operator"));
                break;

            case AdministrationVerb.RestoreBackup:
            {
                if (!TryRead(secrets, error, out var values, "Secret de l'administrateur PostgreSQL"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new RestoreVerifier(trace).RestoreAsync(new BackupRestoreRequest(
                    new PostgreSqlConnectionSettings(options.Require("--host"), options.Port, options.AdminDatabase,
                        options.Require("--admin-user"), values[0], options.Get("--root-certificate")),
                    options.Require("--manifest"),
                    options.Require("--operator"),
                    options.Get("--pg-bin"),
                    options.Require("--target-database"),
                    options.Require("--migrator-role")));
                break;
            }

            case AdministrationVerb.VerifyBackup:
            {
                if (!TryRead(secrets, error, out var values, "Secret de l'administrateur PostgreSQL"))
                {
                    return (int)MigrationExitCode.InvalidArguments;
                }

                result = await new RestoreVerifier(trace).RunAsync(new RestoreVerificationRequest(
                    new PostgreSqlConnectionSettings(options.Require("--host"), options.Port, options.AdminDatabase,
                        options.Require("--admin-user"), values[0], options.Get("--root-certificate")),
                    options.Require("--manifest"),
                    options.Require("--operator"),
                    options.Get("--pg-bin"),
                    options.Get("--scratch-database")));
                break;
            }

            default:
                throw new InvalidOperationException("Verbe d'administration non câblé.");
        }

        (result.Succeeded ? output : error).WriteLine($"code {(int)result.ExitCode} — {result.Message}");
        return (int)result.ExitCode;
    }

    /// <summary>
    /// Connexion du verbe <c>backup</c> : explicite (<c>--host</c>, <c>--database</c>, <c>--username</c> ensemble, secret
    /// sur l'entrée standard) ou, sans aucune de ces options, celle qu'a enregistrée <c>configure-backup</c> (tâche
    /// planifiée, DPAPI). Jamais un mélange des deux.
    /// </summary>
    private static PostgreSqlConnectionSettings? BackupConnection(AdministrationOptions options, SecretInput secrets, TextWriter error)
    {
        var explicitOptions = new[] { "--host", "--database", "--username" }.Count(o => options.Get(o) is not null);
        if (explicitOptions == 3)
        {
            return TryRead(secrets, error, out var values, "Secret du rôle de sauvegarde")
                ? new PostgreSqlConnectionSettings(options.Require("--host"), options.Port, options.Require("--database"),
                    options.Require("--username"), values[0], options.Get("--root-certificate"))
                : null;
        }

        if (explicitOptions > 0 || options.Get("--port") is not null || options.Get("--root-certificate") is not null)
        {
            error.WriteLine("--host, --database et --username vont ensemble ; sans eux, la connexion enregistrée par configure-backup est utilisée.");
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            error.WriteLine("La connexion enregistrée (DPAPI) n'existe que sous Windows : fournir --host, --database et --username.");
            return null;
        }

        var file = BackupConnectionFile.CreateDefault();
        if (!file.Exists)
        {
            error.WriteLine($"Aucune connexion de sauvegarde enregistrée ({file.Path}) : lancer configure-backup sous ce compte Windows.");
            return null;
        }

        try
        {
            return file.Load();
        }
        catch (DatabaseConfigurationException)
        {
            error.WriteLine($"Connexion de sauvegarde enregistrée inutilisable ({file.Path}) : relancer configure-backup sous ce compte Windows.");
            return null;
        }
    }

    private static bool TryRead(SecretInput secrets, TextWriter error, out string[] values, params string[] labels)
    {
        values = new string[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            var value = secrets.Read(labels[i]);
            if (value is null)
            {
                error.WriteLine($"Secret manquant sur l'entrée standard : {labels[i]}.");
                return false;
            }

            values[i] = value;
        }

        return true;
    }
}
