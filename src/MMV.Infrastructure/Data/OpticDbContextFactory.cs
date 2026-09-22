using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MMV.Infrastructure.Configuration;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Factory pour la création du DbContext au moment du design (migrations).
///
/// <para>
/// P4-5E (ADR-PROD-DB-005 §5.5, décision D-05) : la factory choisit la chaîne de migrations visée par
/// <c>dotnet ef</c> selon <see cref="DesignTimeProviderVariableName"/>, et <b>uniquement</b> selon elle :
/// <list type="bullet">
///   <item>variable absente ou vide ⇒ <b>SQLite</b>, comportement historique inchangé (chaîne de
///     <c>MMV.Infrastructure</c>, contrôle de dérive CI existant) ;</item>
///   <item><c>postgresql</c> (alias <c>postgres</c>, <c>npgsql</c>) ⇒ <b>PostgreSQL</b>, chaîne de
///     l'assembly <see cref="DatabaseProviderResolver.PostgreSqlMigrationsAssemblyName"/> ;</item>
///   <item>toute autre valeur ⇒ <see cref="DatabaseConfigurationException"/>, jamais de repli silencieux.</item>
/// </list>
/// Les variables runtime (<see cref="DatabaseProviderResolver.ProviderVariableName"/>,
/// <see cref="DatabaseProviderResolver.ConnectionStringVariableName"/>) ne sont <b>jamais</b> lues ici :
/// générer une migration ne dépend pas de l'environnement d'exécution du poste.
/// </para>
/// </summary>
public class OpticDbContextFactory : IDesignTimeDbContextFactory<OpticDbContext>
{
    /// <summary>
    /// Variable d'environnement <b>design-time</b> choisissant la chaîne de migrations visée par
    /// <c>dotnet ef</c>. Lue par cette factory uniquement ; le runtime l'ignore.
    /// </summary>
    public const string DesignTimeProviderVariableName = "MMV_DESIGNTIME_DATABASE_PROVIDER";

    /// <summary>
    /// Chaîne de connexion PostgreSQL design-time : <b>constante, factice, non secrète</b> (D-05.4).
    /// Ni mot de passe, ni identifiant réel ; l'hôte appartient au domaine réservé <c>.invalid</c>
    /// (RFC 2606), qui ne se résout jamais. Générer une migration, lire le modèle ou produire un script
    /// n'ouvre aucune connexion ; une commande qui se connecterait (<c>database update</c>) échoue donc,
    /// ce qui est voulu (D-05.7).
    /// </summary>
    public const string DesignTimePostgreSqlConnectionString =
        "Host=mmv-design-time.invalid;Database=mmv_design_time";

    public OpticDbContext CreateDbContext(string[] args) => CreateDbContext(environment: null);

    /// <summary>
    /// Crée le contexte design-time.
    /// </summary>
    /// <param name="environment">
    /// Source de variables injectable pour les tests. Si <c>null</c>, l'environnement réel
    /// (<see cref="Environment.GetEnvironmentVariable(string)"/>) est utilisé.
    /// </param>
    /// <exception cref="DatabaseConfigurationException">
    /// Si <see cref="DesignTimeProviderVariableName"/> porte une valeur non reconnue.
    /// </exception>
    public OpticDbContext CreateDbContext(IReadOnlyDictionary<string, string?>? environment)
    {
        string? Get(string key) => environment is not null
            ? (environment.TryGetValue(key, out var value) ? value : null)
            : Environment.GetEnvironmentVariable(key);

        // Même sémantique que la variable runtime (D-05.3) : une faute de frappe lève, et le message nomme
        // la variable design-time sans jamais restituer la valeur reçue.
        var provider = DatabaseProviderResolver.ParseProvider(
            Get(DesignTimeProviderVariableName), DesignTimeProviderVariableName);

        var optionsBuilder = new DbContextOptionsBuilder<OpticDbContext>();

        if (provider == DatabaseProvider.PostgreSql)
        {
            // Aucun chemin SQLite résolu, aucun dossier créé (D-05.5). Configure applique la même
            // sélection que le runtime : Npgsql + assembly de migrations PostgreSQL (D-01.5).
            DatabaseProviderResolver.Configure(optionsBuilder, new DatabaseProviderOptions
            {
                Provider = DatabaseProvider.PostgreSql,
                ConnectionString = DesignTimePostgreSqlConnectionString
            });

            return new OpticDbContext(optionsBuilder.Options);
        }

        // SQLite — comportement historique. Chemin unique résolu par SqliteDatabasePathResolver (P2A-1A).
        var dbPath = SqliteDatabasePathResolver.ResolveDatabasePath(environment: environment);
        SqliteDatabasePathResolver.EnsureDirectoryExists(dbPath);

        DatabaseProviderResolver.Configure(optionsBuilder, DatabaseProviderOptions.Sqlite, dbPath);

        return new OpticDbContext(optionsBuilder.Options);
    }
}
