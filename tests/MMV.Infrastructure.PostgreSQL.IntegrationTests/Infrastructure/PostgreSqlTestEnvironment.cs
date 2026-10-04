namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>
/// Contrat unique de fourniture du serveur (ADR-PROD-DB-008 §5.2) : une variable d'environnement de TEST,
/// distincte de <c>MMV_DATABASE_CONNECTION_STRING</c>. Peu importe d'où vient le serveur (service de CI,
/// installation Windows native, conteneur local) : seule la variable compte.
/// </summary>
public static class PostgreSqlTestEnvironment
{
    /// <summary>Chaîne de connexion du serveur de test. Le rôle doit pouvoir créer et supprimer des bases.</summary>
    public const string ConnectionStringVariableName = "MMV_TEST_POSTGRESQL_CONNECTION_STRING";

    /// <summary>Posée à <c>true</c> par la CI : un serveur absent devient alors un échec, jamais un skip.</summary>
    public const string RequiredVariableName = "MMV_INTEGRATION_REQUIRED";

    /// <summary>
    /// Raison d'ignorer un test, ou <c>null</c> s'il doit s'exécuter. Un test n'est ignoré qu'en local, sans
    /// serveur ; quand <see cref="RequiredVariableName"/> vaut <c>true</c>, il s'exécute toujours (§5.4).
    /// </summary>
    public static string? SkipReason(IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (HasConnectionString(environment) || IsRequired(environment))
        {
            return null;
        }

        return "Comportement PostgreSQL NON PROUVÉ : aucun serveur fourni. Renseignez " +
               $"{ConnectionStringVariableName} (ex. Host=127.0.0.1;Port=5432;Username=…;Password=…) vers un " +
               "serveur PostgreSQL jetable dont le rôle peut créer des bases (CREATEDB).";
    }

    /// <summary>Chaîne de connexion fournie ; lève un message explicite si elle manque.</summary>
    public static string GetRequiredConnectionString(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var value = Get(ConnectionStringVariableName, environment);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariableName} est absente ou vide alors que les tests d'intégration " +
                $"PostgreSQL sont exigés ({RequiredVariableName}=true) : aucun serveur à tester.");
        }

        return value.Trim();
    }

    private static bool HasConnectionString(IReadOnlyDictionary<string, string?>? environment) =>
        !string.IsNullOrWhiteSpace(Get(ConnectionStringVariableName, environment));

    private static bool IsRequired(IReadOnlyDictionary<string, string?>? environment) =>
        string.Equals(Get(RequiredVariableName, environment)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static string? Get(string key, IReadOnlyDictionary<string, string?>? environment) =>
        environment is not null
            ? (environment.TryGetValue(key, out var value) ? value : null)
            : Environment.GetEnvironmentVariable(key);
}

/// <summary>
/// <see cref="FactAttribute"/> d'intégration PostgreSQL : ignoré explicitement en local sans serveur,
/// toujours exécuté en CI (xunit 2 n'offre pas de skip dynamique ; aucun paquet ajouté pour cela).
/// </summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        var reason = PostgreSqlTestEnvironment.SkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }
}

/// <summary>Équivalent de <see cref="PostgreSqlFactAttribute"/> pour les tests paramétrés.</summary>
public sealed class PostgreSqlTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTheoryAttribute()
    {
        var reason = PostgreSqlTestEnvironment.SkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }
}
