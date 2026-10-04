using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

/// <summary>Lecture du catalogue PostgreSQL : le schéma tel que le serveur le détient, pas tel qu'EF le croit.</summary>
public static class SchemaCatalog
{
    /// <summary>
    /// Empreinte ordonnée du schéma <c>public</c> : colonnes (type, nullabilité, défaut), contraintes et
    /// définitions d'index. Deux bases au schéma identique ont des empreintes égales (N1).
    /// </summary>
    public static Task<List<string>> FingerprintAsync(PostgreSqlDatabase database) => QueryAsync(database, """
        SELECT 'column ' || c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
               || CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE ' NULL' END
               || coalesce(' DEFAULT ' || pg_get_expr(d.adbin, d.adrelid), '')
               || coalesce(' IDENTITY ' || nullif(a.attidentity::text, ''), '')
        FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
        WHERE n.nspname = 'public' AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
        UNION ALL
        SELECT 'constraint ' || con.conrelid::regclass::text || ' ' || con.conname || ' ' || pg_get_constraintdef(con.oid)
        FROM pg_constraint con JOIN pg_namespace n ON n.oid = con.connamespace
        WHERE n.nspname = 'public'
        UNION ALL
        SELECT 'index ' || indexdef FROM pg_indexes WHERE schemaname = 'public'
        UNION ALL
        SELECT 'migration ' || "MigrationId" FROM "__EFMigrationsHistory"
        ORDER BY 1
        """);

    /// <summary>Exécute une requête à une colonne texte et renvoie ses lignes, triées.</summary>
    public static async Task<List<string>> QueryAsync(PostgreSqlDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }
}
