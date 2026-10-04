using System.Globalization;
using System.Text;

namespace MMV.DatabaseManager.CommandLine;

/// <summary>Verbes de l'outil. Aucun <c>rollback</c>, <c>drop</c> ni <c>down</c> : le retour arrière est la restauration (P4-9).</summary>
public enum MigrationVerb
{
    /// <summary>Diagnostic en lecture seule : ni verrou, ni écriture.</summary>
    Status,

    /// <summary>Applique la baseline et les migrations ultérieures (DP-1.1).</summary>
    Migrate,

    /// <summary>Initialise la métadonnée sur une base migrée avant l'introduction du journal (Q-22).</summary>
    AdoptCompatibility
}

/// <summary>
/// Options de ligne de commande (P4-6B, C4 ; §2.2 du plan). <b>Aucun défaut permissif</b> : un verbe ou une
/// option inconnus, une option répétée ou sans valeur sont refusés. La chaîne de connexion n'est
/// <b>jamais</b> un argument (historique du shell, liste des processus) : elle vient de
/// <see cref="ConnectionStringVariableName"/>.
/// </summary>
/// <param name="Verb">Verbe demandé.</param>
/// <param name="Operator">Référence d'opérateur (H13) — obligatoire pour les verbes qui écrivent.</param>
/// <param name="BackupReference">Référence de sauvegarde (H12) — son absence est un refus de sauvegarde (code 11).</param>
/// <param name="AppRole">Rôle applicatif recevant la lecture de la métadonnée (Q-23) — obligatoire pour les verbes qui écrivent.</param>
/// <param name="Wait">Attente bornée du verrou.</param>
public sealed record MigrationToolOptions(
    MigrationVerb Verb,
    string? Operator,
    string? BackupReference,
    string? AppRole,
    TimeSpan Wait)
{
    /// <summary>
    /// Chaîne de connexion du rôle <b>migrateur</b>. Distincte de la variable des postes
    /// (<c>MMV_DATABASE_CONNECTION_STRING</c>, rôle applicatif sans DDL) : l'identifiant migrateur n'est
    /// stocké sur aucun poste (DP-5).
    /// </summary>
    public const string ConnectionStringVariableName = "MMV_MIGRATOR_CONNECTION_STRING";

    /// <summary>Longueur maximale d'un identifiant PostgreSQL (NAMEDATALEN − 1) : au-delà, le serveur tronquerait.</summary>
    public const int MaximumIdentifierBytes = 63;

    /// <summary>Attente du verrou par défaut : courte (un outil lancé deux fois doit le dire, pas se figer).</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(10);

    /// <summary>Attente maximale acceptée : jamais d'attente infinie.</summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromHours(1);

    public const string Usage =
        "Usage : MMV.DatabaseManager <status | migrate | adopt-compatibility> " +
        "[--operator <référence>] [--backup-ref <identifiant>] [--app-role <rôle>] [--wait <secondes>]\n" +
        "  migrate / adopt-compatibility : --operator et --app-role obligatoires, --backup-ref exigée.\n" +
        $"  Connexion du rôle migrateur : variable d'environnement {ConnectionStringVariableName}.";

    /// <summary>Analyse les arguments ; ne lève jamais.</summary>
    public static MigrationToolParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return MigrationToolParseResult.Invalid("Verbe manquant.");
        }

        MigrationVerb verb;
        switch (args[0])
        {
            case "status": verb = MigrationVerb.Status; break;
            case "migrate": verb = MigrationVerb.Migrate; break;
            case "adopt-compatibility": verb = MigrationVerb.AdoptCompatibility; break;
            default: return MigrationToolParseResult.Invalid($"Verbe inconnu : '{args[0]}'.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Count; i++)
        {
            var name = args[i];
            if (name is not ("--operator" or "--backup-ref" or "--app-role" or "--wait"))
            {
                return MigrationToolParseResult.Invalid($"Option inconnue : '{name}'.");
            }

            if (i + 1 >= args.Count)
            {
                return MigrationToolParseResult.Invalid($"L'option {name} attend une valeur.");
            }

            if (!values.TryAdd(name, args[++i]))
            {
                return MigrationToolParseResult.Invalid($"L'option {name} est répétée.");
            }
        }

        var wait = DefaultWait;
        if (values.TryGetValue("--wait", out var waitText))
        {
            if (!int.TryParse(waitText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || TimeSpan.FromSeconds(seconds) > MaximumWait)
            {
                return MigrationToolParseResult.Invalid(
                    $"--wait attend un nombre de secondes entre 0 et {MaximumWait.TotalSeconds:0}.");
            }

            wait = TimeSpan.FromSeconds(seconds);
        }

        values.TryGetValue("--operator", out var @operator);
        values.TryGetValue("--backup-ref", out var backupReference);
        values.TryGetValue("--app-role", out var appRole);

        if (@operator is not null && !IsUsableText(@operator))
        {
            return MigrationToolParseResult.Invalid("--operator est vide ou contient un caractère de contrôle.");
        }

        if (backupReference is not null && !IsUsableText(backupReference))
        {
            return MigrationToolParseResult.Invalid("--backup-ref est vide ou contient un caractère de contrôle.");
        }

        if (appRole is not null
            && (!IsUsableText(appRole) || Encoding.UTF8.GetByteCount(appRole) > MaximumIdentifierBytes))
        {
            return MigrationToolParseResult.Invalid(
                $"--app-role est vide, contient un caractère de contrôle ou dépasse {MaximumIdentifierBytes} octets.");
        }

        if (verb != MigrationVerb.Status)
        {
            if (@operator is null)
            {
                return MigrationToolParseResult.Invalid("--operator est obligatoire pour ce verbe (H13).");
            }

            if (appRole is null)
            {
                return MigrationToolParseResult.Invalid("--app-role est obligatoire pour ce verbe (Q-23).");
            }
        }

        return new MigrationToolParseResult(
            new MigrationToolOptions(verb, @operator, backupReference, appRole, wait), null);
    }

    private static bool IsUsableText(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}

/// <summary>Résultat d'analyse : des options valides, ou un message d'erreur.</summary>
public sealed record MigrationToolParseResult(MigrationToolOptions? Options, string? Error)
{
    public bool IsValid => Options is not null;

    internal static MigrationToolParseResult Invalid(string error) => new(null, error);
}
