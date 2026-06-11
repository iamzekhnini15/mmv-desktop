namespace MMV.Infrastructure.Data;

/// <summary>
/// Source unique de résolution du chemin de la base SQLite (P2A-1A).
/// Remplace les chemins dispersés (4 sites de configuration, 2 chemins physiques)
/// par un algorithme unique, configurable par environnement et testable.
///
/// Ordre de priorité :
///   1. chemin explicite fourni par l'appelant ;
///   2. variable d'environnement <see cref="EnvironmentVariableName"/> ;
///   3. valeur de configuration <see cref="ConnectionStringName"/> (Data Source=...) ;
///   4. défaut : %LOCALAPPDATA%\ManageMyVision\mmv.db.
/// </summary>
public static class SqliteDatabasePathResolver
{
    /// <summary>Variable d'environnement permettant de surcharger le chemin du fichier de base.</summary>
    public const string EnvironmentVariableName = "MMV_DATABASE_PATH";

    /// <summary>Nom logique de la chaîne de connexion (configuration).</summary>
    public const string ConnectionStringName = "OpticDatabase";

    /// <summary>Dossier applicatif sous %LOCALAPPDATA%.</summary>
    public const string ApplicationFolderName = "ManageMyVision";

    /// <summary>Nom de fichier par défaut de la base.</summary>
    public const string DefaultFileName = "mmv.db";

    /// <summary>
    /// Ancien nom de fichier relatif utilisé par le runtime historique (<c>Data Source=mmv-optic.db</c>,
    /// relatif au dossier de travail) avant l'unification du chemin (P2A-1A). Sert au diagnostic et à la
    /// reprise contrôlée de P2A-1B. Le runtime courant n'utilise plus ce chemin.
    /// </summary>
    public const string LegacyRelativeFileName = "mmv-optic.db";

    /// <summary>
    /// Résout le chemin absolu de l'ancien fichier de base <c>mmv-optic.db</c> (P2A-1B), relatif au
    /// dossier de base fourni (par défaut, le dossier de travail courant — comportement du runtime
    /// historique). Ne crée ni ne modifie aucun fichier.
    /// </summary>
    /// <param name="baseDirectory">
    /// Dossier de référence (par défaut <see cref="Directory.GetCurrentDirectory"/>).
    /// </param>
    public static string ResolveLegacyDatabasePath(string? baseDirectory = null)
        => Path.GetFullPath(Path.Combine(
            string.IsNullOrWhiteSpace(baseDirectory) ? Directory.GetCurrentDirectory() : baseDirectory,
            LegacyRelativeFileName));

    /// <summary>
    /// Résout le chemin absolu du fichier de base SQLite.
    /// </summary>
    /// <param name="explicitPath">Chemin explicite prioritaire (peut être null).</param>
    /// <param name="environment">
    /// Source de variables d'environnement injectable pour les tests. Si null, l'environnement
    /// réel (<see cref="Environment.GetEnvironmentVariable(string)"/>) est utilisé.
    /// </param>
    /// <param name="configuredConnectionString">
    /// Chaîne de connexion issue de la configuration (ex. appsettings) — au format "Data Source=...".
    /// </param>
    public static string ResolveDatabasePath(
        string? explicitPath = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? configuredConnectionString = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var fromEnv = environment is not null
            ? (environment.TryGetValue(EnvironmentVariableName, out var value) ? value : null)
            : Environment.GetEnvironmentVariable(EnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var path = ExtractDataSource(configuredConnectionString);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ApplicationFolderName,
            DefaultFileName);
    }

    /// <summary>
    /// Construit la chaîne de connexion SQLite à partir du chemin résolu.
    /// </summary>
    public static string GetConnectionString(
        string? explicitPath = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? configuredConnectionString = null)
        => $"Data Source={ResolveDatabasePath(explicitPath, environment, configuredConnectionString)}";

    /// <summary>
    /// Crée le dossier parent du fichier de base s'il n'existe pas. No-op pour les bases en mémoire.
    /// </summary>
    public static void EnsureDirectoryExists(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath) || databasePath == ":memory:")
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string? ExtractDataSource(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            if (key.Equals("Data Source", StringComparison.OrdinalIgnoreCase)
                || key.Equals("DataSource", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Filename", StringComparison.OrdinalIgnoreCase))
            {
                return part[(separator + 1)..].Trim();
            }
        }

        return null;
    }
}
