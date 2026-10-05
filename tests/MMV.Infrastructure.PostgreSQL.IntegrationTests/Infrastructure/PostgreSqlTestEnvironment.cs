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

    /// <summary>
    /// P4-8 (D-14) : serveur <b>TLS</b> de test (chaîne administrateur en <c>SSL Mode=VerifyFull</c> avec son autorité
    /// racine). Fourni en CI par <c>Tls/start-tls-servers.sh</c> ; même règle : absent + exigé ⇒ échec, jamais skip.
    /// </summary>
    public const string TlsConnectionStringVariableName = "MMV_TEST_POSTGRESQL_TLS_CONNECTION_STRING";

    /// <summary>Autorité racine ÉTRANGÈRE au certificat du serveur TLS (preuve de refus de chaîne).</summary>
    public const string TlsWrongCaVariableName = "MMV_TEST_POSTGRESQL_TLS_WRONG_CA";

    /// <summary>Port d'un serveur TLS dont le certificat, signé par la bonne autorité, est expiré.</summary>
    public const string TlsExpiredPortVariableName = "MMV_TEST_POSTGRESQL_TLS_EXPIRED_PORT";

    /// <summary>
    /// P4-9 : dossier des binaires <c>pg_dump</c> / <c>pg_restore</c> de même version majeure que le serveur. Exigé
    /// par tout test de sauvegarde qui s'exécute : absent ⇒ échec, jamais un skip.
    /// </summary>
    public const string PgBinVariableName = "MMV_TEST_PG_BIN";

    /// <summary>Raison d'ignorer un test TLS, ou <c>null</c> s'il doit s'exécuter.</summary>
    public static string? TlsSkipReason(IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(Get(TlsConnectionStringVariableName, environment)) || IsRequired(environment))
        {
            return null;
        }

        return "Comportement PostgreSQL TLS NON PROUVÉ : aucun serveur TLS fourni. Lancez " +
               "Tls/start-tls-servers.sh puis chargez les variables de tls.env.";
    }

    /// <summary>Variable de test TLS obligatoire ; lève un message explicite si elle manque.</summary>
    public static string GetRequired(string variableName, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var value = Get(variableName, environment);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{variableName} est absente ou vide alors que les tests d'intégration PostgreSQL TLS sont exigés.");
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

/// <summary>P4-8 : test exigeant le serveur TLS de test (<see cref="PostgreSqlTestEnvironment.TlsConnectionStringVariableName"/>).</summary>
public sealed class PostgreSqlTlsFactAttribute : FactAttribute
{
    public PostgreSqlTlsFactAttribute()
    {
        var reason = PostgreSqlTestEnvironment.TlsSkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }
}

/// <summary>Équivalent de <see cref="PostgreSqlTlsFactAttribute"/> pour les tests paramétrés.</summary>
public sealed class PostgreSqlTlsTheoryAttribute : TheoryAttribute
{
    public PostgreSqlTlsTheoryAttribute()
    {
        var reason = PostgreSqlTestEnvironment.TlsSkipReason();
        if (reason is not null)
        {
            Skip = reason;
        }
    }
}
