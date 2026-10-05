using System.Data.Common;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MMV.DatabaseManager.Backup;

/// <summary>Nombre exact de lignes d'une table, à l'instant de la lecture.</summary>
public sealed record TableRowCount(string Schema, string Table, long Rows);

/// <summary>
/// État d'une base, lu en une fois (P4-9) : identité (cluster, nom, OID), horloge du serveur, historique EF et
/// nombre de lignes de chaque table. C'est la même empreinte qui est relevée dans l'instantané de la sauvegarde,
/// dans la base de vérification restaurée, puis dans la base réelle juste avant une migration.
/// </summary>
/// <param name="SystemIdentifier">Identifiant du cluster (<c>pg_control_system()</c>) : distingue une installation d'une autre.</param>
/// <param name="Database">Nom de la base.</param>
/// <param name="DatabaseOid">OID de la base : distingue une base recréée sous le même nom.</param>
/// <param name="ServerTimeUtc">Horloge du serveur (<c>now()</c>), jamais celle du poste (ADR-PROD-DB-004).</param>
/// <param name="AppliedMigrations">Historique EF, ordre ordinal.</param>
/// <param name="Tables">Tables ordinaires et partitionnées hors catalogues, ordre ordinal (schéma, table).</param>
public sealed record DatabaseFingerprint(
    string SystemIdentifier,
    string Database,
    long DatabaseOid,
    DateTimeOffset ServerTimeUtc,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<TableRowCount> Tables);

/// <summary>Port de lecture de l'état de la base (étape 3 de <see cref="MigrationRunner"/>).</summary>
public interface IDatabaseFingerprintReader
{
    Task<DatabaseFingerprint> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Lecture de <see cref="DatabaseFingerprint"/> sur une connexion <b>déjà ouverte</b>, en lecture seule. Dans une
/// transaction <c>REPEATABLE READ</c>, toutes les valeurs (dont <c>now()</c>) appartiennent au même instantané.
/// Les noms de table viennent du serveur (<c>format('%I.%I')</c>) : aucun identifiant n'est cité côté client.
/// </summary>
public sealed class DatabaseFingerprintReader : IDatabaseFingerprintReader
{
    private const string HistoryTable = "public.\"" + HistoryRepository.DefaultTableName + "\"";

    private readonly DbConnection _connection;

    public DatabaseFingerprintReader(DbConnection connection) =>
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public async Task<DatabaseFingerprint> ReadAsync(CancellationToken cancellationToken = default)
    {
        string systemIdentifier;
        string database;
        long oid;
        DateTimeOffset now;
        bool hasHistory;
        await using (var command = ServerCommand.Create(_connection,
                         "SELECT (SELECT system_identifier::text FROM pg_catalog.pg_control_system()), current_database(), " +
                         "(SELECT oid::bigint FROM pg_catalog.pg_database WHERE datname = current_database()), now(), " +
                         "to_regclass(@history) IS NOT NULL",
                         ("history", HistoryTable)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            systemIdentifier = reader.GetString(0);
            database = reader.GetString(1);
            oid = reader.GetInt64(2);
            now = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc));
            hasHistory = reader.GetBoolean(4);
        }

        var applied = new List<string>();
        if (hasHistory)
        {
            await using var command = ServerCommand.Create(_connection, $"SELECT \"MigrationId\" FROM {HistoryTable}");
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }

        var relations = new List<(string Schema, string Table, string Qualified)>();
        await using (var command = ServerCommand.Create(_connection,
                         "SELECT n.nspname::text, c.relname::text, format('%I.%I', n.nspname, c.relname) " +
                         "FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace " +
                         "WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema') " +
                         "AND n.nspname NOT LIKE 'pg\\_toast%' AND n.nspname NOT LIKE 'pg\\_temp\\_%'"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                relations.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var tables = new List<TableRowCount>(relations.Count);
        foreach (var (schema, table, qualified) in relations)
        {
            var rows = Convert.ToInt64(await ServerCommand.ScalarAsync(_connection, $"SELECT count(*) FROM {qualified}", cancellationToken));
            tables.Add(new TableRowCount(schema, table, rows));
        }

        applied.Sort(StringComparer.Ordinal);
        return new DatabaseFingerprint(systemIdentifier, database, oid, now, applied,
            tables.OrderBy(t => t.Schema, StringComparer.Ordinal).ThenBy(t => t.Table, StringComparer.Ordinal).ToArray());
    }
}
