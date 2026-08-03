using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Source unique de sélection du fournisseur de base de données et de configuration EF associée (P4-3).
/// Suit la convention de <see cref="Data.SqliteDatabasePathResolver"/> et de <see cref="SeedOptionsResolver"/> :
/// variables d'environnement préfixées <c>MMV_</c>, source injectable pour les tests, défaut sûr.
///
/// Principes :
///   * <b>défaut SQLite</b> : variable absente ou vide ⇒ <see cref="DatabaseProvider.Sqlite"/>, soit
///     exactement le comportement historique (développement, tests, démonstration, mono-poste local) ;
///   * <b>configuration invalide bloquée</b> : un nom de fournisseur inconnu (faute de frappe) lève une
///     <see cref="DatabaseConfigurationException"/> et ne retombe <b>jamais</b> silencieusement sur SQLite ;
///   * <b>secrets hors dépôt</b> : la chaîne de connexion serveur provient d'une variable d'environnement,
///     n'est jamais écrite dans le dépôt, et n'est jamais restituée dans un message d'erreur.
///
/// <para>
/// Périmètre P4-3 : cette classe <b>sélectionne</b> le fournisseur EF, rien de plus. Aucun retry, aucun
/// mapping <c>DateTime</c>, aucun mapping monétaire, aucune migration serveur — ces sujets appartiennent
/// à P4-4/P4-5 et restent ouverts (cf. ADR-PROD-DB-002).
/// </para>
/// </summary>
public static class DatabaseProviderResolver
{
    /// <summary>Variable d'environnement sélectionnant le fournisseur (<c>sqlite</c> / <c>postgresql</c>).</summary>
    public const string ProviderVariableName = "MMV_DATABASE_PROVIDER";

    /// <summary>
    /// Variable d'environnement portant la chaîne de connexion <b>serveur</b> (secret, hors dépôt).
    /// Obligatoire pour <see cref="DatabaseProvider.PostgreSql"/>, ignorée pour SQLite.
    /// </summary>
    public const string ConnectionStringVariableName = "MMV_DATABASE_CONNECTION_STRING";

    /// <summary>
    /// Résout les <see cref="DatabaseProviderOptions"/> depuis l'environnement.
    /// </summary>
    /// <param name="environment">
    /// Source de variables injectable pour les tests. Si <c>null</c>, l'environnement réel
    /// (<see cref="Environment.GetEnvironmentVariable(string)"/>) est utilisé.
    /// </param>
    /// <exception cref="DatabaseConfigurationException">
    /// Si le fournisseur demandé est inconnu, ou si PostgreSQL est demandé sans chaîne de connexion.
    /// </exception>
    public static DatabaseProviderOptions Resolve(IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Get(string key) => environment is not null
            ? (environment.TryGetValue(key, out var value) ? value : null)
            : Environment.GetEnvironmentVariable(key);

        var provider = ParseProvider(Get(ProviderVariableName));

        if (provider == DatabaseProvider.Sqlite)
        {
            // Le chemin du fichier SQLite reste résolu par SqliteDatabasePathResolver (MMV_DATABASE_PATH) :
            // aucune chaîne de connexion serveur n'est requise ni retenue ici.
            return DatabaseProviderOptions.Sqlite;
        }

        var connectionString = Get(ConnectionStringVariableName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Configuration serveur incomplète ⇒ blocage explicite (jamais de repli sur SQLite, qui
            // ferait démarrer un poste sur une base locale isolée en croyant être connecté au serveur).
            throw new DatabaseConfigurationException(
                $"Le fournisseur '{provider}' est sélectionné via {ProviderVariableName}, mais la variable " +
                $"{ConnectionStringVariableName} est absente ou vide. Renseignez la chaîne de connexion " +
                "serveur (hors dépôt) : aucun repli sur SQLite n'est effectué.");
        }

        return new DatabaseProviderOptions
        {
            Provider = provider,
            ConnectionString = connectionString.Trim()
        };
    }

    /// <summary>
    /// Configure le <paramref name="optionsBuilder"/> EF pour le fournisseur résolu.
    /// <b>Sélection du fournisseur uniquement</b> : aucune option de résilience, de mapping ou de
    /// migration n'est appliquée ici (hors périmètre P4-3).
    /// </summary>
    /// <param name="optionsBuilder">Builder EF à configurer.</param>
    /// <param name="providerOptions">Fournisseur et chaîne de connexion résolus.</param>
    /// <param name="sqliteDatabasePath">
    /// Chemin du fichier SQLite déjà résolu par l'appelant. Ignoré hors SQLite ; si <c>null</c> alors que
    /// SQLite est sélectionné, le chemin est résolu par <see cref="SqliteDatabasePathResolver"/>.
    /// </param>
    /// <exception cref="DatabaseConfigurationException">
    /// Si PostgreSQL est demandé sans chaîne de connexion exploitable.
    /// </exception>
    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        DatabaseProviderOptions providerOptions,
        string? sqliteDatabasePath = null)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(providerOptions);

        switch (providerOptions.Provider)
        {
            case DatabaseProvider.Sqlite:
                // Format de connexion inchangé depuis P2A-1A : "Data Source=<chemin résolu>".
                optionsBuilder.UseSqlite(
                    SqliteDatabasePathResolver.GetConnectionString(sqliteDatabasePath));
                break;

            case DatabaseProvider.PostgreSql:
                if (!providerOptions.HasConnectionString)
                {
                    throw new DatabaseConfigurationException(
                        $"Le fournisseur '{DatabaseProvider.PostgreSql}' requiert une chaîne de connexion " +
                        $"(variable {ConnectionStringVariableName}), absente des options fournies.");
                }

                optionsBuilder.UseNpgsql(providerOptions.ConnectionString);
                break;

            default:
                throw new DatabaseConfigurationException(
                    $"Fournisseur de base de données non pris en charge : '{providerOptions.Provider}'.");
        }
    }

    /// <summary>
    /// Convertit une valeur d'environnement en <see cref="DatabaseProvider"/>. Tolérante à la casse,
    /// aux espaces et aux alias usuels. Valeur absente ou vide ⇒ <see cref="DatabaseProvider.Sqlite"/> ;
    /// toute autre valeur non reconnue ⇒ <see cref="DatabaseConfigurationException"/>.
    /// </summary>
    private static DatabaseProvider ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DatabaseProvider.Sqlite;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "sqlite" => DatabaseProvider.Sqlite,
            "postgresql" or "postgres" or "npgsql" => DatabaseProvider.PostgreSql,
            // Une faute de frappe ne doit jamais retomber silencieusement sur SQLite : on bloque.
            // La valeur reçue n'est jamais restituée : la variable peut avoir été renseignée par erreur
            // avec une chaîne de connexion ou un identifiant, et le message peut finir dans un journal.
            _ => throw new DatabaseConfigurationException(
                $"Valeur invalide dans {ProviderVariableName}. " +
                "Valeurs acceptées : 'sqlite' (défaut), 'postgresql' " +
                "(alias 'postgres', 'npgsql').")
        };
    }
}
