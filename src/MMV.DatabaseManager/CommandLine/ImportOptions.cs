using System.Globalization;

namespace MMV.DatabaseManager.CommandLine;

/// <summary>
/// Options de <c>import-sqlite</c> (P4-7). Même discipline que <see cref="MigrationToolOptions"/> : <b>aucun défaut
/// permissif</b> — option inconnue, répétée ou sans valeur refusée ; le fuseau d'origine n'a pas de valeur par défaut
/// (ADR-004 décision 8). La connexion est celle du rôle <b>migrateur</b>
/// (<see cref="MigrationToolOptions.ConnectionStringVariableName"/>), jamais un argument.
/// </summary>
public sealed record ImportOptions(
    string Source,
    string SourceTimeZone,
    string Operator,
    string BackupReference,
    string Report,
    bool DryRun,
    TimeSpan Wait)
{
    public const string Verb = "import-sqlite";

    public const string Usage =
        "Usage : MMV.DatabaseManager import-sqlite --source <copie à froid .db> --source-time-zone <fuseau du magasin> " +
        "--operator <référence> --backup-ref <manifeste vérifié de la cible> --report <fichier .json neuf> " +
        "[--dry-run] [--wait <secondes>]\n" +
        $"  Connexion du rôle migrateur : variable d'environnement {MigrationToolOptions.ConnectionStringVariableName}.";

    private static readonly string[] Required = ["--source", "--source-time-zone", "--operator", "--backup-ref", "--report"];

    public static bool IsImportVerb(IReadOnlyList<string> args) => args.Count > 0 && args[0] == Verb;

    /// <summary>Analyse les arguments ; ne lève jamais.</summary>
    public static ImportParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!IsImportVerb(args))
        {
            return ImportParseResult.Invalid("Verbe import-sqlite attendu.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var dryRun = false;
        for (var i = 1; i < args.Count; i++)
        {
            var name = args[i];
            if (name == "--dry-run")
            {
                if (dryRun)
                {
                    return ImportParseResult.Invalid("L'option --dry-run est répétée.");
                }

                dryRun = true;
                continue;
            }

            if (!Required.Contains(name, StringComparer.Ordinal) && name != "--wait")
            {
                return ImportParseResult.Invalid($"Option inconnue : '{name}'.");
            }

            if (i + 1 >= args.Count)
            {
                return ImportParseResult.Invalid($"L'option {name} attend une valeur.");
            }

            var value = args[++i];
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            {
                return ImportParseResult.Invalid($"L'option {name} est vide ou contient un caractère de contrôle.");
            }

            if (!values.TryAdd(name, value))
            {
                return ImportParseResult.Invalid($"L'option {name} est répétée.");
            }
        }

        var missing = Required.FirstOrDefault(r => !values.ContainsKey(r));
        if (missing is not null)
        {
            return ImportParseResult.Invalid($"{missing} est obligatoire.");
        }

        var wait = MigrationToolOptions.DefaultWait;
        if (values.TryGetValue("--wait", out var waitText))
        {
            if (!int.TryParse(waitText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || TimeSpan.FromSeconds(seconds) > MigrationToolOptions.MaximumWait)
            {
                return ImportParseResult.Invalid(
                    $"--wait attend un nombre de secondes entre 0 et {MigrationToolOptions.MaximumWait.TotalSeconds:0}.");
            }

            wait = TimeSpan.FromSeconds(seconds);
        }

        return new ImportParseResult(new ImportOptions(values["--source"], values["--source-time-zone"], values["--operator"],
            values["--backup-ref"], values["--report"], dryRun, wait), null);
    }
}

/// <summary>Résultat d'analyse : des options valides, ou un message d'erreur.</summary>
public sealed record ImportParseResult(ImportOptions? Options, string? Error)
{
    public bool IsValid => Options is not null;

    internal static ImportParseResult Invalid(string error) => new(null, error);
}
