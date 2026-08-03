namespace MMV.Infrastructure.Configuration;

/// <summary>
/// Paramètres typés désignant le fournisseur de base de données à utiliser (P4-3).
/// Construits par <see cref="DatabaseProviderResolver"/> à partir de l'environnement, ou fournis
/// directement (design-time, tests). Le défaut sûr est <see cref="DatabaseProvider.Sqlite"/>.
/// </summary>
public sealed class DatabaseProviderOptions
{
    /// <summary>Options SQLite explicites — défaut historique, sans chaîne de connexion serveur.</summary>
    public static readonly DatabaseProviderOptions Sqlite = new()
    {
        Provider = DatabaseProvider.Sqlite
    };

    /// <summary>Fournisseur résolu. Défaut : <see cref="DatabaseProvider.Sqlite"/>.</summary>
    public DatabaseProvider Provider { get; init; } = DatabaseProvider.Sqlite;

    /// <summary>
    /// Chaîne de connexion <b>serveur</b> (PostgreSQL uniquement), issue de
    /// <see cref="DatabaseProviderResolver.ConnectionStringVariableName"/>.
    ///
    /// <para>
    /// Toujours <c>null</c> pour <see cref="DatabaseProvider.Sqlite"/> : le chemin du fichier SQLite
    /// reste la propriété exclusive de <see cref="Data.SqliteDatabasePathResolver"/> et de la variable
    /// <c>MMV_DATABASE_PATH</c>, que P4-3 ne remplace pas.
    /// </para>
    ///
    /// <para><b>Secret</b> : ne jamais journaliser ni sérialiser cette valeur.</para>
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>Indique si une chaîne de connexion serveur exploitable est disponible.</summary>
    public bool HasConnectionString => !string.IsNullOrWhiteSpace(ConnectionString);
}
