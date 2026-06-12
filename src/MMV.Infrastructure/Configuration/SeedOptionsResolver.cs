using MMV.Domain.Validators;

namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Source unique de résolution des <see cref="SeedOptions"/> à partir de l'environnement (P2A-1F).
/// Suit la convention de <see cref="Data.SqliteDatabasePathResolver"/> : variables d'environnement
/// préfixées <c>MMV_</c>, source injectable pour les tests, et défaut sûr.
///
/// Principes :
///   * <b>défaut sûr</b> : environnement absent ou non reconnu ⇒ <see cref="ApplicationEnvironment.Production"/>
///     (aucun seed de démonstration, aucun compte faible) — « configuration absente/ambiguë : comportement sûr » ;
///   * <b>secrets hors dépôt</b> : le mot de passe bootstrap provient d'une variable d'environnement, jamais du code ;
///   * <b>configuration invalide bloquée</b> : un mot de passe bootstrap fourni mais ne respectant pas la
///     politique forte lève une <see cref="SeedConfigurationException"/>.
/// </summary>
public static class SeedOptionsResolver
{
    /// <summary>Variable d'environnement sélectionnant l'environnement (Production/Development/Demonstration/Test).</summary>
    public const string EnvironmentVariableName = "MMV_ENVIRONMENT";

    /// <summary>Variable d'environnement activant explicitement le jeu de démonstration (booléen).</summary>
    public const string EnableDemoSeedVariableName = "MMV_ENABLE_DEMO_SEED";

    /// <summary>Variable d'environnement du nom d'utilisateur administrateur bootstrap.</summary>
    public const string BootstrapAdminUsernameVariableName = "MMV_BOOTSTRAP_ADMIN_USERNAME";

    /// <summary>Variable d'environnement du mot de passe administrateur bootstrap (secret, hors dépôt).</summary>
    public const string BootstrapAdminPasswordVariableName = "MMV_BOOTSTRAP_ADMIN_PASSWORD";

    /// <summary>Nom d'utilisateur bootstrap par défaut.</summary>
    public const string DefaultBootstrapAdminUsername = "admin";

    /// <summary>
    /// Résout les <see cref="SeedOptions"/> depuis l'environnement.
    /// </summary>
    /// <param name="environment">
    /// Source de variables injectable pour les tests. Si <c>null</c>, l'environnement réel
    /// (<see cref="Environment.GetEnvironmentVariable(string)"/>) est utilisé.
    /// </param>
    /// <exception cref="SeedConfigurationException">
    /// Si un mot de passe bootstrap est fourni mais ne respecte pas la politique de mot de passe forte.
    /// </exception>
    public static SeedOptions Resolve(IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Get(string key) => environment is not null
            ? (environment.TryGetValue(key, out var value) ? value : null)
            : Environment.GetEnvironmentVariable(key);

        var applicationEnvironment = ParseEnvironment(Get(EnvironmentVariableName));
        var demoRequested = ParseBoolean(Get(EnableDemoSeedVariableName));

        var username = Get(BootstrapAdminUsernameVariableName);
        if (string.IsNullOrWhiteSpace(username))
        {
            username = DefaultBootstrapAdminUsername;
        }

        var password = Get(BootstrapAdminPasswordVariableName);
        if (!string.IsNullOrWhiteSpace(password))
        {
            // Secret fourni mais invalide ⇒ blocage explicite (compte bootstrap sécurisé).
            var (isValid, errorMessage) = UserValidator.ValidatePasswordPolicy(password);
            if (!isValid)
            {
                throw new SeedConfigurationException(
                    $"Le mot de passe administrateur bootstrap fourni via {BootstrapAdminPasswordVariableName} " +
                    $"est invalide : {errorMessage} Le démarrage est bloqué (aucun compte faible n'est créé).");
            }
        }
        else
        {
            password = null;
        }

        return new SeedOptions
        {
            Environment = applicationEnvironment,
            // Défense en profondeur : le seed de démonstration n'est jamais activé hors Development/Demonstration.
            EnableDemoSeed = demoRequested,
            BootstrapAdminUsername = username.Trim(),
            BootstrapAdminPassword = password
        };
    }

    /// <summary>
    /// Convertit une valeur d'environnement en <see cref="ApplicationEnvironment"/>. Tolérante aux alias
    /// usuels ; toute valeur absente ou non reconnue retombe sur <see cref="ApplicationEnvironment.Production"/>
    /// (défaut sûr).
    /// </summary>
    public static ApplicationEnvironment ParseEnvironment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ApplicationEnvironment.Production;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "production" or "prod" => ApplicationEnvironment.Production,
            "development" or "dev" => ApplicationEnvironment.Development,
            "demonstration" or "demo" => ApplicationEnvironment.Demonstration,
            "test" or "testing" => ApplicationEnvironment.Test,
            _ => ApplicationEnvironment.Production
        };
    }

    private static bool ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false
        };
    }
}
