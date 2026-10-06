using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.Import.TestSupport;

/// <summary>
/// Base SQLite source jetable pour les tests P4-7 : créée par la chaîne de migrations SQLite de <b>production</b>
/// (historique complet, données <c>HasData</c>), puis remplie <b>par le modèle</b> — chaque colonne de chaque table,
/// clés étrangères cohérentes (ligne <c>i</c> de l'enfant → ligne <c>i</c> du parent), identifiants non contigus,
/// NULL une ligne sur quatre, textes non ASCII, montants <c>REAL</c>, dates civiles au format historique une ligne
/// sur deux, instants en heure locale sans fuseau (format EF). Fichier partagé (lié) par les tests unitaires de l'outil.
/// </summary>
public sealed class SqliteSourceBuilder : IDisposable
{
    /// <summary>Heure locale de la ligne 0 (instants) ; ligne <c>i</c> = + <c>i</c> minutes.</summary>
    public static readonly DateTime BaseLocalTime = new(2026, 6, 15, 10, 30, 0);

    private readonly string _directory;

    private SqliteSourceBuilder(string directory)
    {
        _directory = directory;
        Path = System.IO.Path.Combine(directory, "mmv-source.db");
    }

    public string Path { get; }

    /// <summary>Base migrée par la chaîne SQLite de production, sans autre donnée que les <c>HasData</c>.</summary>
    public static SqliteSourceBuilder CreateMigrated()
    {
        var builder = new SqliteSourceBuilder(Directory.CreateTempSubdirectory("mmv-import-").FullName);
        using (var context = builder.Context())
        {
            context.Database.Migrate();
        }

        SqliteConnection.ClearAllPools();
        builder.EnsureCold();
        return builder;
    }

    /// <summary>Clé entière de la ligne <c>i</c> d'une table à clé entière propre : non contiguë, jamais 1.</summary>
    public static long IntegerKey(int i) => 7 + 3L * i;

    public OpticDbContext Context()
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>();
        DatabaseProviderResolver.Configure(options, new DatabaseProviderOptions { Provider = DatabaseProvider.Sqlite }, Path);
        return new OpticDbContext(options.Options);
    }

    /// <summary>Remplit chaque table de <paramref name="rows"/> lignes ; compteur ORDER des HasData porté à 42.</summary>
    public void Populate(int rows)
    {
        using var context = Context();
        var model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();

        foreach (var table in Ordered(model.Tables))
        {
            var columns = table.Columns.ToArray();
            // Libérée à chaque table (P4-12) : une commande abandonnée garde ses instructions préparées jusqu'à leur
            // finalisation par le GC ; fermée entre-temps, la connexion devient « zombie », SQLite ne fait pas le
            // point de contrôle et le journal -wal reste — la source serait refusée comme base non fermée (CI
            // 37459291267). Reproduit à coup sûr par un GC.Collect() avant la fermeture.
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO \"{table.Name}\" ({string.Join(", ", columns.Select(c => $"\"{c.Name}\""))}) " +
                                  $"VALUES ({string.Join(", ", columns.Select((_, k) => "$p" + k))})";
            var parameters = columns.Select((_, k) => command.Parameters.Add(new SqliteParameter("$p" + k, null))).ToArray();
            for (var i = 0; i < rows; i++)
            {
                for (var k = 0; k < columns.Length; k++)
                {
                    parameters[k].Value = Value(table, columns[k], i) ?? DBNull.Value;
                }

                command.ExecuteNonQuery();
            }
        }

        Execute(connection, transaction, "UPDATE \"DocumentSequences\" SET \"CurrentValue\" = 42 WHERE \"SequenceName\" = 'ORDER'");
        transaction.Commit();
        connection.Close();
        EnsureCold();
    }

    /// <summary>SQL brut sur la source ; <paramref name="foreignKeys"/> = false permet d'écrire des orphelins.</summary>
    public void Execute(string sql, bool foreignKeys = true)
    {
        using (var connection = Open(foreignKeys))
        {
            Execute(connection, null, sql);
        }

        EnsureCold();
    }

    /// <summary>
    /// Toute écriture du builder rend une source <b>froide</b> (aucun journal non vide) : un journal restant
    /// signale une connexion encore ouverte et ferait refuser la source pour une autre raison que celle testée.
    /// </summary>
    private void EnsureCold()
    {
        foreach (var suffix in new[] { "-journal", "-wal" })
        {
            var side = new FileInfo(Path + suffix);
            if (side.Exists && side.Length > 0)
            {
                throw new InvalidOperationException($"Source de test non froide : {side.Name} ({side.Length} octets) après fermeture.");
            }
        }
    }

    public List<string> Strings(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values;
    }

    public T Scalar<T>(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Fichier encore tenu par un pool d'un autre test : le dossier temporaire sera purgé par le système.
        }
    }

    private SqliteConnection Open(bool foreignKeys = true)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Pooling = false,
            ForeignKeys = foreignKeys
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static IEnumerable<ITable> Ordered(IEnumerable<ITable> tables)
    {
        var pending = tables.Where(t => t.Name != "__EFMigrationsHistory").OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        var done = new HashSet<string>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var next = pending.First(t => t.ForeignKeyConstraints.All(fk => fk.PrincipalTable == t || done.Contains(fk.PrincipalTable.Name)));
            pending.Remove(next);
            done.Add(next.Name);
            yield return next;
        }
    }

    private static object? Value(ITable table, IColumn column, int i)
    {
        var isKey = table.PrimaryKey!.Columns.Contains(column);
        if (!isKey && column.IsNullable && i % 4 == 3)
        {
            return null;
        }

        var fk = table.ForeignKeyConstraints.FirstOrDefault(f => f.Columns.Contains(column));
        if (fk is not null)
        {
            var principal = fk.PrincipalColumns[fk.Columns.ToList().IndexOf(column)];
            return Value(fk.PrincipalTable, principal, i);
        }

        var property = column.PropertyMappings.First().Property;
        var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clr.IsEnum)
        {
            var names = Enum.GetNames(clr);
            return names[i % names.Length];
        }

        if (clr == typeof(string))
        {
            var text = isKey ? $"SEQ{i}" : $"{column.Name[..Math.Min(column.Name.Length, 6)]}-{i}-é€ü";
            var max = property.GetMaxLength();
            return max is { } m && text.Length > m ? text[^m..] : text;
        }

        if (clr == typeof(int) || clr == typeof(long) || clr == typeof(short))
        {
            return isKey ? IntegerKey(i) : i + 1L;
        }

        if (clr == typeof(bool))
        {
            return (long)(i % 2);
        }

        if (clr == typeof(decimal))
        {
            var amount = (i % 9) + 0.125;
            return column.StoreType.Equals("REAL", StringComparison.OrdinalIgnoreCase)
                ? amount
                : amount.ToString("R", CultureInfo.InvariantCulture);
        }

        if (clr == typeof(double) || clr == typeof(float))
        {
            return i + 0.25;
        }

        if (clr == typeof(DateTime))
        {
            var local = BaseLocalTime.AddMinutes(i);
            return i % 2 == 0
                ? local.AddTicks(1234567).ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)
                : local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (clr == typeof(DateOnly))
        {
            return i % 2 == 0 ? "1985-03-15" : "1985-03-15 00:00:00";
        }

        throw new NotSupportedException($"Type {clr.Name} de {table.Name}.{column.Name} non généré.");
    }
}
