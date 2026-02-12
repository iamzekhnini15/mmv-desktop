using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;

namespace MMV.App.Services;

/// <summary>
/// Service d'exécution de requêtes SQL générées par l'IA.
/// Restreint strictement aux requêtes SELECT en lecture seule.
/// </summary>
public class SqlExecutorService
{
    private readonly OpticDbContext _dbContext;

    /// <summary>
    /// Liste de mots-clés SQL interdits (opérations d'écriture).
    /// </summary>
    private static readonly string[] ForbiddenKeywords = new[]
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
        "TRUNCATE", "REPLACE", "MERGE", "EXEC", "EXECUTE",
        "ATTACH", "DETACH", "PRAGMA", "VACUUM", "REINDEX",
        "GRANT", "REVOKE", "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT"
    };

    public SqlExecutorService(OpticDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <summary>
    /// Exécute une requête SQL SELECT et retourne les résultats sous forme de DataTable.
    /// </summary>
    /// <param name="sql">La requête SQL à exécuter.</param>
    /// <param name="cancellationToken">Token d'annulation.</param>
    /// <returns>Un DataTable contenant les résultats.</returns>
    /// <exception cref="InvalidOperationException">Si la requête n'est pas une requête SELECT valide.</exception>
    public async Task<DataTable> ExecuteQueryAsync(string sql, CancellationToken cancellationToken = default)
    {
        ValidateQuery(sql);

        var dataTable = new DataTable();

        // Obtenir la connexion depuis le DbContext
        var connection = _dbContext.Database.GetDbConnection();

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;

            using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // Construire les colonnes
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName = reader.GetName(i);
                // Éviter les doublons de noms de colonnes
                if (dataTable.Columns.Contains(columnName))
                {
                    columnName = $"{columnName}_{i}";
                }
                dataTable.Columns.Add(columnName, typeof(object));
            }

            // Lire les lignes
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = dataTable.NewRow();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                }
                dataTable.Rows.Add(row);
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Erreur lors de l'exécution de la requête SQL : {ex.Message}", ex);
        }

        return dataTable;
    }

    /// <summary>
    /// Valide que la requête est strictement en lecture seule (SELECT uniquement).
    /// </summary>
    /// <param name="sql">La requête SQL à valider.</param>
    /// <exception cref="InvalidOperationException">Si la requête contient des opérations interdites.</exception>
    private static void ValidateQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException("La requête SQL est vide.");
        }

        var normalized = sql.Trim();

        // La requête doit commencer par SELECT ou WITH (pour les CTE)
        if (!normalized.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Seules les requêtes SELECT sont autorisées. " +
                $"La requête commence par : \"{normalized.Substring(0, Math.Min(20, normalized.Length))}...\"");
        }

        // Vérifier l'absence de mots-clés d'écriture
        // On tokenise pour éviter les faux positifs dans les noms de colonnes
        var upperSql = normalized.ToUpperInvariant();
        foreach (var keyword in ForbiddenKeywords)
        {
            var index = 0;
            while ((index = upperSql.IndexOf(keyword, index, StringComparison.Ordinal)) >= 0)
            {
                // Vérifier que c'est un mot complet (pas partie d'un identifiant)
                var before = index > 0 ? upperSql[index - 1] : ' ';
                var after = index + keyword.Length < upperSql.Length ? upperSql[index + keyword.Length] : ' ';

                if (!char.IsLetterOrDigit(before) && before != '_' &&
                    !char.IsLetterOrDigit(after) && after != '_')
                {
                    throw new InvalidOperationException(
                        $"Opération interdite détectée : '{keyword}'. Seules les requêtes SELECT en lecture seule sont autorisées.");
                }

                index += keyword.Length;
            }
        }

        // Vérifier l'absence de points-virgules multiples (injection)
        var semicolonCount = normalized.Count(c => c == ';');
        if (semicolonCount > 1)
        {
            throw new InvalidOperationException(
                "Requête SQL suspecte : plusieurs instructions détectées. Seule une requête SELECT unique est autorisée.");
        }
    }
}
