using System.Data;
using System.Data.Common;

namespace MMV.DatabaseManager;

/// <summary>
/// Exécution SQL sur <see cref="DbConnection"/>, paramètres créés par la commande elle-même — aucun type de
/// provider (précédent P4-1 Lot D).
/// </summary>
internal static class ServerCommand
{
    /// <summary>
    /// Exige une connexion déjà ouverte : les composants de l'outil ne rouvrent <b>jamais</b> la connexion de
    /// contrôle. Une connexion perdue a perdu le verrou de session ; rouvrir permettrait d'écrire hors verrou.
    /// </summary>
    public static DbConnection RequireOpen(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "Connexion de contrôle fermée ou perdue : aucune opération n'est tentée hors de la session du verrou.");
        }

        return connection;
    }

    public static async Task<object?> ScalarAsync(
        DbConnection connection, string sql, CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Create(connection, sql, parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public static async Task<int> NonQueryAsync(
        DbConnection connection, string sql, CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = Create(connection, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Instruction dont des <b>identifiants</b> ou littéraux viennent de l'exploitant (P4-8 ; motif de
    /// <c>ApplicationRoleGrants</c>, Q-23) : le texte est produit par le serveur, <c>format('… %I … %L', @x)</c>,
    /// valeurs en paramètres liés — <b>jamais concaténées</b> côté client — puis exécuté tel quel.
    /// </summary>
    public static async Task ExecuteServerFormattedAsync(
        DbConnection connection, string formatSql, CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var statement = await ScalarAsync(connection, formatSql, cancellationToken, parameters) as string
                        ?? throw new InvalidOperationException("Le serveur n'a pas produit l'instruction attendue.");

        await NonQueryAsync(connection, statement, cancellationToken);
    }

    public static DbCommand Create(DbConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = RequireOpen(connection).CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    /// <summary>
    /// Serveur injoignable ou authentification refusée : <see cref="DbException"/> sans SQLSTATE (réseau, DNS,
    /// délai), ou classes <c>08</c> (connexion), <c>28</c> (autorisation), <c>3D000</c> (base inexistante).
    /// </summary>
    public static bool IsServerUnreachable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException db)
            {
                var state = db.SqlState;
                return state is null || state.StartsWith("08", StringComparison.Ordinal)
                                     || state.StartsWith("28", StringComparison.Ordinal)
                                     || state == "3D000";
            }
        }

        return false;
    }

    /// <summary>Cause lisible d'un échec, sans chaîne de connexion : type, SQLSTATE éventuel, message.</summary>
    public static string Describe(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } state } db)
            {
                return $"{state}: {db.Message}";
            }
        }

        return $"{exception.GetType().Name}: {exception.Message}";
    }
}
