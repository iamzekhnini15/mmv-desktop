using System.Globalization;
using MMV.Infrastructure.Configuration;

namespace MMV.DatabaseManager.CommandLine;

/// <summary>Verbes d'administration P4-8 (ADR-PROD-DB-010) — distincts des verbes de migration P4-6B.</summary>
public enum AdministrationVerb
{
    /// <summary>Crée ou fait converger la base et les trois rôles (D-09, DP-5). Identifiant administrateur.</summary>
    Provision,

    /// <summary>Remplace le secret d'un rôle MMV (D-14, rotation). Identifiant administrateur.</summary>
    RotateRolePassword,

    /// <summary>Crée le premier administrateur applicatif, une seule fois (D-12). Rôle migrateur.</summary>
    BootstrapAdmin,

    /// <summary>Écrit la configuration protégée (DPAPI) d'un poste après l'avoir vérifiée (D-13).</summary>
    ConfigureWorkstation,

    /// <summary>P4-9 : sauvegarde <c>pg_dump</c> + manifeste. Rôle de sauvegarde (lecture seule).</summary>
    Backup,

    /// <summary>P4-9 : restauration réelle dans une base de vérification, puis preuve. Identifiant administrateur.</summary>
    VerifyBackup,

    /// <summary>P4-9 : enregistre (DPAPI) la connexion du rôle de sauvegarde pour la tâche planifiée.</summary>
    ConfigureBackup,

    /// <summary>P4-9 : applique la rétention ; seul chemin de suppression des sauvegardes. Aucun serveur.</summary>
    PruneBackups,

    /// <summary>P4-9 : reprise après sinistre — sauvegarde vérifiée restaurée dans une base NEUVE. Administrateur.</summary>
    RestoreBackup
}

/// <summary>
/// Options des verbes P4-8. Mêmes règles que <see cref="MigrationToolOptions"/> : <b>aucun défaut permissif</b>,
/// option inconnue, répétée ou vide refusée. <b>Aucun secret n'est jamais un argument</b> (historique du shell,
/// liste des processus) : les secrets sont lus sur l'entrée standard, masqués en mode interactif.
/// </summary>
public sealed record AdministrationOptions(
    AdministrationVerb Verb,
    IReadOnlyDictionary<string, string> Values)
{
    public const int DefaultPort = 5432;
    public const string DefaultAdminDatabase = "postgres";

    public const string Usage =
        "Verbes d'administration (P4-8) — secrets lus sur l'entrée standard, jamais en argument :\n" +
        "  provision --host <h> [--port <p>] [--root-certificate <ca.crt>] --admin-user <u> [--admin-database <db>]\n" +
        "            --database <db> --migrator-role <r> --app-role <r> --backup-role <r> --operator <ref>\n" +
        "      secrets : administrateur, migrateur, applicatif, sauvegarde\n" +
        "  rotate-role-password --host <h> [--port <p>] [--root-certificate <ca.crt>] --admin-user <u>\n" +
        "            [--admin-database <db>] --database <db> --role <r> --operator <ref>\n" +
        "      secrets : administrateur, nouveau secret du rôle\n" +
        "  bootstrap-admin --username <login> --operator <ref>   (connexion : rôle migrateur, " +
        MigrationToolOptions.ConnectionStringVariableName + ")\n" +
        "      secrets : mot de passe initial, confirmation\n" +
        "  configure-workstation --host <h> [--port <p>] [--root-certificate <ca.crt>] --database <db> --username <r>\n" +
        "      secret : secret du rôle applicatif\n" +
        "Sauvegarde (P4-9) — chemins absolus :\n" +
        "  backup [--host <h> [--port <p>] [--root-certificate <ca.crt>] --database <db> --username <rôle de sauvegarde>]\n" +
        "            --output-directory <dossier> --operator <ref> [--pg-bin <dossier>]\n" +
        "      secret : rôle de sauvegarde ; sans --host/--database/--username : connexion enregistrée par configure-backup\n" +
        "  configure-backup --host <h> [--port <p>] [--root-certificate <ca.crt>] --database <db> --username <rôle de sauvegarde>\n" +
        "      secret : rôle de sauvegarde (lancé sous le compte Windows de la tâche planifiée)\n" +
        "  verify-backup --host <h> [--port <p>] [--root-certificate <ca.crt>] --admin-user <u> [--admin-database <db>]\n" +
        "            --manifest <fichier .manifest.json> --operator <ref> [--pg-bin <dossier>] [--scratch-database <db>]\n" +
        "      secret : administrateur\n" +
        "  restore-backup --host <h> [--port <p>] [--root-certificate <ca.crt>] --admin-user <u> [--admin-database <db>]\n" +
        "            --manifest <fichier .manifest.json> --target-database <base neuve> --migrator-role <r> --operator <ref>\n" +
        "            [--pg-bin <dossier>]\n" +
        "      secret : administrateur\n" +
        "  prune-backups --directory <dossier> --operator <ref>";

    private static readonly IReadOnlyDictionary<AdministrationVerb, (string[] Required, string[] Optional)> Shapes =
        new Dictionary<AdministrationVerb, (string[], string[])>
        {
            [AdministrationVerb.Provision] = (
                ["--host", "--admin-user", "--database", "--migrator-role", "--app-role", "--backup-role", "--operator"],
                ["--port", "--root-certificate", "--admin-database"]),
            [AdministrationVerb.RotateRolePassword] = (
                ["--host", "--admin-user", "--database", "--role", "--operator"],
                ["--port", "--root-certificate", "--admin-database"]),
            [AdministrationVerb.BootstrapAdmin] = (["--username", "--operator"], []),
            [AdministrationVerb.ConfigureWorkstation] = (
                ["--host", "--database", "--username"],
                ["--port", "--root-certificate"]),
            [AdministrationVerb.Backup] = (
                ["--output-directory", "--operator"],
                ["--host", "--port", "--root-certificate", "--database", "--username", "--pg-bin"]),
            [AdministrationVerb.VerifyBackup] = (
                ["--host", "--admin-user", "--manifest", "--operator"],
                ["--port", "--root-certificate", "--admin-database", "--pg-bin", "--scratch-database"]),
            [AdministrationVerb.ConfigureBackup] = (
                ["--host", "--database", "--username"],
                ["--port", "--root-certificate"]),
            [AdministrationVerb.PruneBackups] = (["--directory", "--operator"], []),
            [AdministrationVerb.RestoreBackup] = (
                ["--host", "--admin-user", "--manifest", "--target-database", "--migrator-role", "--operator"],
                ["--port", "--root-certificate", "--admin-database", "--pg-bin"])
        };

    /// <summary>Le premier argument désigne-t-il un verbe P4-8 ?</summary>
    public static bool IsAdministrationVerb(IReadOnlyList<string> args) =>
        args.Count > 0 && TryVerb(args[0], out _);

    public string? Get(string name) => Values.GetValueOrDefault(name);

    public string Require(string name) => Values[name];

    public int Port => Values.TryGetValue("--port", out var port)
        ? int.Parse(port, NumberStyles.None, CultureInfo.InvariantCulture)
        : DefaultPort;

    public string AdminDatabase => Get("--admin-database") ?? DefaultAdminDatabase;

    /// <summary>Analyse les arguments ; ne lève jamais.</summary>
    public static AdministrationParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0 || !TryVerb(args[0], out var verb))
        {
            return AdministrationParseResult.Invalid("Verbe d'administration inconnu ou manquant.");
        }

        var (required, optional) = Shapes[verb];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i++)
        {
            var name = args[i];
            if (!required.Contains(name) && !optional.Contains(name))
            {
                return AdministrationParseResult.Invalid($"Option inconnue pour ce verbe : '{name}'.");
            }

            if (i + 1 >= args.Count)
            {
                return AdministrationParseResult.Invalid($"L'option {name} attend une valeur.");
            }

            var value = args[++i];
            if (!PostgreSqlConnectionSettings.IsUsableText(value))
            {
                return AdministrationParseResult.Invalid($"L'option {name} est vide ou contient un caractère de contrôle.");
            }

            if (!values.TryAdd(name, value))
            {
                return AdministrationParseResult.Invalid($"L'option {name} est répétée.");
            }
        }

        var missing = required.Where(r => !values.ContainsKey(r)).ToArray();
        if (missing.Length > 0)
        {
            return AdministrationParseResult.Invalid($"Option(s) obligatoire(s) manquante(s) : {string.Join(", ", missing)}.");
        }

        if (values.TryGetValue("--port", out var portText)
            && (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535))
        {
            return AdministrationParseResult.Invalid("--port attend un entier entre 1 et 65535.");
        }

        foreach (var pathOption in new[] { "--root-certificate", "--output-directory", "--manifest", "--pg-bin", "--directory" })
        {
            if (values.TryGetValue(pathOption, out var path) && !Path.IsPathFullyQualified(path))
            {
                return AdministrationParseResult.Invalid($"{pathOption} attend un chemin absolu.");
            }
        }

        foreach (var identifier in new[] { "--database", "--admin-database", "--migrator-role", "--app-role", "--backup-role", "--role", "--admin-user", "--username", "--scratch-database", "--target-database" })
        {
            if (values.TryGetValue(identifier, out var name) && !PostgreSqlConnectionSettings.IsUsableIdentifier(name))
            {
                return AdministrationParseResult.Invalid(
                    $"{identifier} dépasse {PostgreSqlConnectionSettings.MaximumIdentifierBytes} octets.");
            }
        }

        return new AdministrationParseResult(new AdministrationOptions(verb, values), null);
    }

    private static bool TryVerb(string text, out AdministrationVerb verb)
    {
        switch (text)
        {
            case "provision": verb = AdministrationVerb.Provision; return true;
            case "rotate-role-password": verb = AdministrationVerb.RotateRolePassword; return true;
            case "bootstrap-admin": verb = AdministrationVerb.BootstrapAdmin; return true;
            case "configure-workstation": verb = AdministrationVerb.ConfigureWorkstation; return true;
            case "backup": verb = AdministrationVerb.Backup; return true;
            case "verify-backup": verb = AdministrationVerb.VerifyBackup; return true;
            case "configure-backup": verb = AdministrationVerb.ConfigureBackup; return true;
            case "prune-backups": verb = AdministrationVerb.PruneBackups; return true;
            case "restore-backup": verb = AdministrationVerb.RestoreBackup; return true;
            default: verb = default; return false;
        }
    }
}

/// <summary>Résultat d'analyse : des options valides, ou un message d'erreur.</summary>
public sealed record AdministrationParseResult(AdministrationOptions? Options, string? Error)
{
    public bool IsValid => Options is not null;

    internal static AdministrationParseResult Invalid(string error) => new(null, error);
}
