using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Import;

/// <summary>
/// Base SQLite source d'un import (P4-7), ouverte en <b>lecture seule</b> (<c>Mode=ReadOnly</c> : le fichier n'est
/// jamais créé ni modifié) et lue dans <b>une</b> transaction, donc sur un instantané unique. Le contexte est
/// configuré par le chemin de production (<see cref="DatabaseProviderResolver"/>) : la liste des migrations SQLite
/// connues est celle du binaire, et aucun type de provider n'apparaît ici.
/// </summary>
public sealed class SqliteImportSource : IAsyncDisposable
{
    private readonly OpticDbContext _context;
    private readonly DbConnection _connection;
    private DbTransaction? _snapshot;

    private SqliteImportSource(string path, OpticDbContext context)
    {
        Path = path;
        _context = context;
        _connection = context.Database.GetDbConnection();
    }

    public string Path { get; }

    /// <summary>Migrations SQLite connues du binaire, ordre ordinal.</summary>
    public IReadOnlyList<string> KnownMigrations =>
        _context.Database.GetMigrations().OrderBy(m => m, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Refus avant ouverture : fichier absent, ou base non fermée (journal <c>-journal</c> ou <c>-wal</c> non vide) —
    /// une copie <b>à froid</b> est exigée, l'outil ne rejoue jamais un journal.
    /// </summary>
    public static string? CheckCold(string path)
    {
        if (!File.Exists(path))
        {
            return $"fichier source introuvable : {path}";
        }

        foreach (var suffix in new[] { "-journal", "-wal" })
        {
            var side = new FileInfo(path + suffix);
            if (side.Exists && side.Length > 0)
            {
                return $"journal SQLite '{side.Name}' présent : base non fermée — fermer MMV et importer une copie à froid";
            }
        }

        return null;
    }

    public static async Task<SqliteImportSource> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var builder = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(builder, new DatabaseProviderOptions { Provider = DatabaseProvider.Sqlite }, path);
        var context = new OpticDbContext(builder.Options);
        var source = new SqliteImportSource(path, context);
        try
        {
            var settings = new DbConnectionStringBuilder { ConnectionString = source._connection.ConnectionString };
            settings["Mode"] = "ReadOnly";
            settings["Pooling"] = "False";
            source._connection.ConnectionString = settings.ConnectionString;
            await source._connection.OpenAsync(cancellationToken);
            source._snapshot = await source._connection.BeginTransactionAsync(cancellationToken);
            return source;
        }
        catch
        {
            await source.DisposeAsync();
            throw;
        }
    }

    /// <summary>Empreinte SHA-256 et taille du fichier source, consignées au rapport.</summary>
    public static async Task<(string Sha256, long Bytes)> HashAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (Convert.ToHexString(hash).ToLowerInvariant(), stream.Length);
    }

    /// <summary><c>PRAGMA integrity_check</c> : <c>null</c> si « ok », sinon les premiers constats.</summary>
    public async Task<string?> IntegrityProblemAsync(CancellationToken cancellationToken = default)
    {
        var lines = await StringsAsync("PRAGMA integrity_check", cancellationToken);
        return lines is ["ok"] ? null : string.Join(" ; ", lines.Take(5));
    }

    /// <summary><c>PRAGMA foreign_key_check</c> : lignes orphelines (table, rowid, table parente).</summary>
    public async Task<IReadOnlyList<string>> ForeignKeyViolationsAsync(CancellationToken cancellationToken = default)
    {
        var violations = new List<string>();
        await using var command = Command("PRAGMA foreign_key_check");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            violations.Add(string.Create(CultureInfo.InvariantCulture,
                $"{reader.GetString(0)} rowid {(reader.IsDBNull(1) ? "?" : reader.GetValue(1))} → {reader.GetString(2)}"));
        }

        return violations;
    }

    /// <summary>Historique EF de la source ; vide si la table n'existe pas.</summary>
    public async Task<IReadOnlyList<string>> AppliedMigrationsAsync(CancellationToken cancellationToken = default)
    {
        var tables = await TablesAsync(cancellationToken);
        if (!tables.Contains(HistoryRepository.DefaultTableName, StringComparer.Ordinal))
        {
            return [];
        }

        var applied = await StringsAsync($"SELECT \"MigrationId\" FROM {Quote(HistoryRepository.DefaultTableName)}", cancellationToken);
        return applied.OrderBy(m => m, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Tables utilisateur (hors tables internes <c>sqlite_*</c>).</summary>
    public async Task<IReadOnlyList<string>> TablesAsync(CancellationToken cancellationToken = default) =>
        await StringsAsync("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name",
            cancellationToken);

    public async Task<IReadOnlyList<string>> ColumnsAsync(string table, CancellationToken cancellationToken = default) =>
        await StringsAsync($"SELECT name FROM pragma_table_info({Literal(table)}) ORDER BY name", cancellationToken);

    public async Task<long> CountAsync(string table, CancellationToken cancellationToken = default)
    {
        await using var command = Command($"SELECT count(*) FROM {Quote(table)}");
        return System.Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>Valeurs brutes d'une colonne non nulle, pour savoir si une colonne ou table inconnue porte des données.</summary>
    public async Task<long> NonNullCountAsync(string table, string column, CancellationToken cancellationToken = default)
    {
        await using var command = Command($"SELECT count({Quote(column)}) FROM {Quote(table)}");
        return System.Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lignes brutes de <paramref name="table"/>, colonnes dans l'ordre du plan, triées par clé primaire en ordre
    /// binaire (<c>COLLATE BINARY</c> = ordre des octets UTF-8, identique à <c>COLLATE "C"</c> côté serveur).
    /// </summary>
    public async IAsyncEnumerable<object?[]> RowsAsync(ImportTable table, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var columns = string.Join(", ", table.Columns.Select(c => Quote(c.Name)));
        var order = string.Join(", ", table.KeyColumns.Select(c =>
            c.Kind == ImportColumnKind.Text ? Quote(c.Name) + " COLLATE BINARY" : Quote(c.Name)));
        await using var command = Command($"SELECT {columns} FROM {Quote(table.Name)} ORDER BY {order}");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[table.Columns.Count];
            for (var i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            yield return row;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_snapshot is not null)
        {
            await _snapshot.DisposeAsync();
        }

        await _connection.CloseAsync();
        await _context.DisposeAsync();
    }

    private DbCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _snapshot;
        return command;
    }

    private async Task<List<string>> StringsAsync(string sql, CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using var command = Command(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
}
