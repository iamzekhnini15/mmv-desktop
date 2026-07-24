using Microsoft.Data.SqlClient;
using Npgsql;

namespace MMV.P4.ProviderComparison.Support;

public enum ProviderKind
{
    Postgres,
    SqlServer
}

/// <summary>
/// Lecture des chaînes de connexion du spike depuis l'ENVIRONNEMENT UNIQUEMENT.
/// Aucun secret n'est écrit dans le dépôt, ni journalisé, ni exposé par ce type :
/// <see cref="Describe"/> ne rend que provider / hôte / port / base / présence du secret.
/// </summary>
public static class SpikeEnvironment
{
    public const string PostgresVariable = "MMV_P4_POSTGRES_CONNECTION";
    public const string SqlServerVariable = "MMV_P4_SQLSERVER_CONNECTION";

    public static string? Postgres => Normalize(Environment.GetEnvironmentVariable(PostgresVariable));

    public static string? SqlServer => Normalize(Environment.GetEnvironmentVariable(SqlServerVariable));

    public static string? For(ProviderKind provider) => provider switch
    {
        ProviderKind.Postgres => Postgres,
        ProviderKind.SqlServer => SqlServer,
        _ => null
    };

    public static bool IsConfigured(ProviderKind provider) => For(provider) is not null;

    /// <summary>
    /// Raison de SKIP lisible, sans jamais divulguer de secret.
    /// </summary>
    public static string SkipReason(ProviderKind provider) => provider switch
    {
        ProviderKind.Postgres => $"{PostgresVariable} non définie — expérimentation PostgreSQL NON EXÉCUTÉE.",
        ProviderKind.SqlServer => $"{SqlServerVariable} non définie — expérimentation SQL Server NON EXÉCUTÉE.",
        _ => "Provider inconnu."
    };

    /// <summary>
    /// Description NON SENSIBLE de la configuration (brief §9) : jamais de mot de passe,
    /// jamais de chaîne complète.
    /// </summary>
    public static string Describe(ProviderKind provider)
    {
        var raw = For(provider);
        if (raw is null)
        {
            return $"{provider}: NON CONFIGURÉ";
        }

        if (provider == ProviderKind.Postgres)
        {
            var b = new NpgsqlConnectionStringBuilder(raw);
            return $"Postgres: host={Mask(b.Host)} port={b.Port} database={b.Database} " +
                   $"user={(string.IsNullOrEmpty(b.Username) ? "absent" : "présent")} " +
                   $"password={(string.IsNullOrEmpty(b.Password) ? "ABSENT" : "PRÉSENT (masqué)")}";
        }

        var s = new SqlConnectionStringBuilder(raw);
        return $"SqlServer: server={Mask(s.DataSource)} database={s.InitialCatalog} " +
               $"user={(string.IsNullOrEmpty(s.UserID) ? "absent/intégré" : "présent")} " +
               $"password={(string.IsNullOrEmpty(s.Password) ? "ABSENT" : "PRÉSENT (masqué)")}";
    }

    private static string Mask(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "(inconnu)";
        }

        // Un hôte local peut porter un port ("localhost,14333") : on ne masque que l'hôte distant.
        var hostOnly = host.Split(',')[0].Trim();
        return hostOnly is "localhost" or "127.0.0.1" or "::1" or "(local)" ? host : "(hôte masqué)";
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
