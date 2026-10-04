using System.Data;
using System.Data.Common;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Lecture <b>seule</b> de <c>mmv_meta.schema_compatibility</c> (P4-6B, DP-3.5), en SQL brut sur
/// <see cref="DbConnection"/> : aucune entité EF, aucun type de provider (précédent P4-1 Lot D). Aucune
/// écriture n'existe dans ce type — <c>MMV.App</c> référence Infrastructure, et tout chemin d'écriture placé
/// ici serait atteignable depuis un poste (H11).
/// </summary>
public static class ServerCompatibilityMetadataReader
{
    internal const string RelationExistsSql = "SELECT to_regclass(@relation) IS NOT NULL";

    private const string RowSql =
        "SELECT schema_version, minimum_supported_version, maintenance_started_at FROM "
        + ServerCompatibilityMetadataNames.QualifiedCompatibilityTable + " WHERE id = 1";

    /// <summary>
    /// Lit la métadonnée sur une connexion <b>ouverte</b>. Une erreur serveur (droits, connexion) est propagée,
    /// jamais avalée.
    /// </summary>
    public static async Task<ServerCompatibilityMetadata> ReadAsync(
        DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!await RelationExistsAsync(
                connection, ServerCompatibilityMetadataNames.QualifiedCompatibilityTable, cancellationToken))
        {
            return ServerCompatibilityMetadata.Absent;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = RowSql;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return ServerCompatibilityMetadata.NoRow;
        }

        return ServerCompatibilityMetadata.FromRow(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : ToDateTimeOffset(reader.GetValue(2)));
    }

    /// <summary>
    /// <c>to_regclass</c> : <c>NULL</c> si la relation (ou son schéma) n'existe pas. Le nom est un paramètre
    /// lié, jamais concaténé.
    /// </summary>
    internal static async Task<bool> RelationExistsAsync(
        DbConnection connection, string relation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = RelationExistsSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "relation";
        parameter.Value = relation;
        command.Parameters.Add(parameter);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static DateTimeOffset ToDateTimeOffset(object value) => value switch
    {
        DateTimeOffset offset => offset,
        // timestamptz est restitué en UTC par Npgsql.
        DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new InvalidCastException(
            $"maintenance_started_at : type inattendu {value.GetType().Name}.")
    };
}
