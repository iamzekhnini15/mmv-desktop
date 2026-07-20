using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MMV.Infrastructure.Data;

/// <summary>
/// Résultat de la vérification de compatibilité de schéma (P2A-1A-R2).
/// </summary>
public sealed class SchemaCompatibilityResult
{
    /// <summary>Liste lisible des divergences bloquantes (vide si compatible).</summary>
    public IReadOnlyList<string> Differences { get; init; } = Array.Empty<string>();

    /// <summary>Vrai si aucune divergence significative n'est détectée.</summary>
    public bool IsCompatible => Differences.Count == 0;
}

/// <summary>
/// Portail de compatibilité de schéma (P2A-1A-R2) : compare le schéma SQLite RÉEL d'une base
/// historique au schéma ATTENDU par le modèle EF courant, AVANT toute écriture de
/// <c>__EFMigrationsHistory</c>. Empêche de transformer une base inconnue/ancienne en base
/// « migrée » par simple inscription des migrations.
///
/// Comparaison pragmatique adaptée à SQLite : ne signale que les éléments ATTENDUS manquants ou
/// incompatibles (les éléments supplémentaires de la base sont tolérés pour éviter les faux positifs).
/// Vérifie : tables attendues, colonnes, types (affinité SQLite), nullabilité, clés primaires,
/// clés étrangères essentielles, index uniques essentiels.
/// </summary>
public sealed class SqliteSchemaVerifier
{
    /// <summary>
    /// Tables <b>purement additives</b> introduites par une migration récente et qu'aucune base historique
    /// antérieure ne peut contenir. Leur absence n'est <b>pas</b> une incompatibilité : la migration qui les
    /// crée est <b>exécutée</b> pendant l'adoption (cf. <see cref="SqliteDatabaseManager"/>), elles sont donc
    /// physiquement créées sans risque. Toute autre table manquante reste bloquante (corruption/schéma ancien).
    /// <list type="bullet">
    ///   <item><c>DocumentSequences</c> — P2A-1E (R-03), créée par <c>AddDocumentSequences</c>.</item>
    ///   <item><c>WorkshopSheets</c> et <c>WorkshopSheetItems</c> — P3-6B (fiche atelier versionnée), créées par
    ///   <c>AddWorkshopSheets</c>. Purement additives : aucune table métier existante n'est modifiée, et aucune
    ///   fiche n'est backfillée (une base antérieure n'a simplement aucune fiche).</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> AdditiveTablesToleratedWhenAbsent =
        new(StringComparer.OrdinalIgnoreCase) { "DocumentSequences", "WorkshopSheets", "WorkshopSheetItems" };

    /// <summary>
    /// Colonnes <b>purement additives</b> (NOT NULL avec valeur par défaut) introduites par une migration
    /// récente et qu'aucune base historique antérieure ne peut contenir. Même principe que
    /// <see cref="AdditiveTablesToleratedWhenAbsent"/>, au niveau colonne : leur absence n'est <b>pas</b> une
    /// incompatibilité, car la migration qui les ajoute est <b>exécutée</b> (et non baselinée) pendant
    /// l'adoption (cf. <see cref="SqliteDatabaseManager"/>), qui vérifie ensuite leur présence physique.
    /// Toute autre colonne manquante reste bloquante (schéma ancien/corrompu).
    /// <list type="bullet">
    ///   <item><c>Customers.IsArchived</c> — P3-2B, ajoutée par <c>AddCustomerArchivingAndProtectHistory</c>.</item>
    ///   <item><c>Products.NormalizedReference</c> — P3-4B, ajoutée par <c>AddProductNormalizedReferenceAndProtectHistory</c>.</item>
    ///   <item><c>Notifications.ResolvedAt</c> — P3-8, ajoutée par <c>AddNotificationResolution</c>. Nullable et sans
    ///   valeur par défaut : une base antérieure n'a simplement aucune résolution enregistrée.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> AdditiveColumnsToleratedWhenAbsent =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Customers.IsArchived",
            "Products.NormalizedReference",
            "Notifications.ResolvedAt",
        };

    /// <summary>
    /// Index uniques <b>purement additifs</b> introduits par une migration récente et qu'aucune base historique
    /// antérieure ne peut contenir. Même principe que <see cref="AdditiveColumnsToleratedWhenAbsent"/> : leur
    /// absence n'est <b>pas</b> une incompatibilité, car la migration qui les crée est <b>exécutée</b> (et non
    /// baselinée) pendant l'adoption. Clé = <c>Table.Col1,Col2</c> (colonnes triées).
    /// <list type="bullet">
    ///   <item><c>Products.NormalizedReference</c> — P3-4B (unicité normalisée), créé par
    ///   <c>AddProductNormalizedReferenceAndProtectHistory</c>. L'ancien index unique sur <c>Products.Reference</c>
    ///   subsiste alors en base : c'est un élément supplémentaire, toléré (non attendu par le modèle courant).</item>
    ///   <item><c>Notifications.EntityId,EntityType,Type</c> — P3-8 (index unique <b>filtré</b> protégeant l'unicité
    ///   des alertes de stock bas actives), créé par <c>AddNotificationResolution</c> après dédoublonnage des
    ///   alertes historiques.</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> AdditiveUniqueIndexesToleratedWhenAbsent =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Products.NormalizedReference",
            "Notifications.EntityId,EntityType,Type",
        };

    public SchemaCompatibilityResult Verify(OpticDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var expectedTables = BuildExpectedSchema(context);
        var actualTables = ReadActualSchema(context);
        var differences = new List<string>();

        foreach (var expected in expectedTables)
        {
            if (!actualTables.TryGetValue(expected.Name, out var actual))
            {
                // Une table additive récente (créée par une migration en attente) n'est pas une divergence
                // bloquante : l'adoption exécutera la migration qui la crée. Les autres tables manquantes
                // (cœur du modèle) restent bloquantes.
                if (!AdditiveTablesToleratedWhenAbsent.Contains(expected.Name))
                {
                    differences.Add($"table manquante: {expected.Name}");
                }

                continue;
            }

            foreach (var column in expected.Columns)
            {
                if (!actual.Columns.TryGetValue(column.Name, out var actualColumn))
                {
                    // Colonne additive récente (ajoutée par une migration en attente) : l'adoption exécutera
                    // cette migration, qui la créera physiquement. Non bloquant.
                    if (!AdditiveColumnsToleratedWhenAbsent.Contains($"{expected.Name}.{column.Name}"))
                    {
                        differences.Add($"colonne manquante: {expected.Name}.{column.Name}");
                    }

                    continue;
                }

                if (!TypesCompatible(column.StoreType, actualColumn.DeclaredType))
                {
                    differences.Add(
                        $"type incompatible: {expected.Name}.{column.Name} " +
                        $"(attendu '{column.StoreType}', réel '{actualColumn.DeclaredType}')");
                }

                if (!column.IsNullable && actualColumn.IsNullable)
                {
                    differences.Add($"nullabilité incompatible: {expected.Name}.{column.Name} (attendu NOT NULL, réel NULL)");
                }
            }

            if (expected.PrimaryKeyColumns.Count > 0
                && !SetEquals(expected.PrimaryKeyColumns, actual.PrimaryKeyColumns))
            {
                differences.Add(
                    $"clé primaire incompatible: {expected.Name} " +
                    $"(attendu [{string.Join(",", expected.PrimaryKeyColumns)}], réel [{string.Join(",", actual.PrimaryKeyColumns)}])");
            }

            foreach (var foreignKey in expected.ForeignKeys)
            {
                var matched = actual.ForeignKeys.Any(actualFk =>
                    string.Equals(actualFk.PrincipalTable, foreignKey.PrincipalTable, StringComparison.OrdinalIgnoreCase)
                    && SetEquals(actualFk.Columns, foreignKey.Columns));

                if (!matched)
                {
                    differences.Add(
                        $"clé étrangère manquante: {expected.Name}({string.Join(",", foreignKey.Columns)}) -> {foreignKey.PrincipalTable}");
                }
            }

            foreach (var uniqueIndex in expected.UniqueIndexes)
            {
                var matched = actual.UniqueIndexes.Any(actualIndex => SetEquals(actualIndex, uniqueIndex));
                if (!matched)
                {
                    // Un index unique additif récent (créé par une migration en attente) n'est pas une divergence
                    // bloquante : l'adoption exécutera la migration qui le crée. Clé sur colonnes triées.
                    var key = $"{expected.Name}.{string.Join(",", uniqueIndex.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))}";
                    if (!AdditiveUniqueIndexesToleratedWhenAbsent.Contains(key))
                    {
                        differences.Add($"index unique manquant: {expected.Name}({string.Join(",", uniqueIndex)})");
                    }
                }
            }
        }

        return new SchemaCompatibilityResult { Differences = differences };
    }

    // ----------------- Schéma ATTENDU (modèle EF) -----------------

    private static List<ExpectedTable> BuildExpectedSchema(OpticDbContext context)
    {
        var relationalModel = context.Model.GetRelationalModel();
        var tables = new List<ExpectedTable>();

        foreach (var table in relationalModel.Tables)
        {
            if (IsInternalTable(table.Name))
            {
                continue;
            }

            var columns = table.Columns
                .Select(c => new ExpectedColumn(c.Name, c.StoreType, c.IsNullable))
                .ToList();

            var primaryKey = table.PrimaryKey?.Columns.Select(c => c.Name).ToList() ?? new List<string>();

            var foreignKeys = table.ForeignKeyConstraints
                .Select(fk => new ExpectedForeignKey(
                    fk.Columns.Select(c => c.Name).ToList(),
                    fk.PrincipalTable.Name))
                .ToList();

            var uniqueIndexes = table.Indexes
                .Where(i => i.IsUnique)
                .Select(i => i.Columns.Select(c => c.Name).ToList())
                .ToList();

            tables.Add(new ExpectedTable(table.Name, columns, primaryKey, foreignKeys, uniqueIndexes));
        }

        return tables;
    }

    // ----------------- Schéma RÉEL (PRAGMA SQLite) -----------------

    private static Dictionary<string, ActualTable> ReadActualSchema(OpticDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;
        try
        {
            if (mustClose)
            {
                connection.Open();
            }

            var tables = new Dictionary<string, ActualTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var tableName in ReadTableNames(connection))
            {
                if (IsInternalTable(tableName))
                {
                    continue;
                }

                tables[tableName] = new ActualTable(
                    ReadColumns(connection, tableName, out var primaryKey),
                    primaryKey,
                    ReadForeignKeys(connection, tableName),
                    ReadUniqueIndexes(connection, tableName));
            }

            return tables;
        }
        finally
        {
            if (mustClose && connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
        }
    }

    private static List<string> ReadTableNames(System.Data.Common.DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static Dictionary<string, ActualColumn> ReadColumns(
        System.Data.Common.DbConnection connection, string tableName, out List<string> primaryKey)
    {
        var columns = new Dictionary<string, ActualColumn>(StringComparer.OrdinalIgnoreCase);
        var pkOrder = new List<(int Position, string Name)>();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);          // name
            var type = reader.GetString(2);          // type
            var notNull = reader.GetInt32(3) != 0;   // notnull
            var pkPosition = reader.GetInt32(5);     // pk (0 = non-PK, sinon position 1-based)

            columns[name] = new ActualColumn(type, !notNull);
            if (pkPosition > 0)
            {
                pkOrder.Add((pkPosition, name));
            }
        }

        primaryKey = pkOrder.OrderBy(p => p.Position).Select(p => p.Name).ToList();
        return columns;
    }

    private static List<ActualForeignKey> ReadForeignKeys(System.Data.Common.DbConnection connection, string tableName)
    {
        var byId = new Dictionary<long, (string PrincipalTable, List<(int Seq, string Column)> Columns)>();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{tableName}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);            // id
            var seq = reader.GetInt32(1);           // seq
            var principalTable = reader.GetString(2); // table (principal)
            var fromColumn = reader.GetString(3);   // from (colonne locale)

            if (!byId.TryGetValue(id, out var entry))
            {
                entry = (principalTable, new List<(int, string)>());
                byId[id] = entry;
            }

            entry.Columns.Add((seq, fromColumn));
        }

        return byId.Values
            .Select(e => new ActualForeignKey(
                e.Columns.OrderBy(c => c.Seq).Select(c => c.Column).ToList(),
                e.PrincipalTable))
            .ToList();
    }

    private static List<List<string>> ReadUniqueIndexes(System.Data.Common.DbConnection connection, string tableName)
    {
        var uniqueIndexNames = new List<string>();
        using (var listCommand = connection.CreateCommand())
        {
            listCommand.CommandText = $"PRAGMA index_list(\"{tableName}\")";
            using var reader = listCommand.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);     // name
                var unique = reader.GetInt32(2) != 0; // unique
                var origin = reader.GetString(3);   // origin: 'c', 'u', 'pk'
                if (unique && !string.Equals(origin, "pk", StringComparison.OrdinalIgnoreCase))
                {
                    uniqueIndexNames.Add(name);
                }
            }
        }

        var result = new List<List<string>>();
        foreach (var indexName in uniqueIndexNames)
        {
            using var infoCommand = connection.CreateCommand();
            infoCommand.CommandText = $"PRAGMA index_info(\"{indexName}\")";
            using var reader = infoCommand.ExecuteReader();
            var columns = new List<(int Seq, string Name)>();
            while (reader.Read())
            {
                var seq = reader.GetInt32(0);       // seqno
                if (!reader.IsDBNull(2))
                {
                    columns.Add((seq, reader.GetString(2))); // name
                }
            }

            result.Add(columns.OrderBy(c => c.Seq).Select(c => c.Name).ToList());
        }

        return result;
    }

    // ----------------- Comparaisons -----------------

    private static bool TypesCompatible(string expectedStoreType, string actualDeclaredType)
        => Affinity(expectedStoreType) == Affinity(actualDeclaredType);

    /// <summary>Affinité de type SQLite (règles de détermination d'affinité de SQLite).</summary>
    private static string Affinity(string? declaredType)
    {
        var type = (declaredType ?? string.Empty).ToUpperInvariant();
        if (type.Contains("INT"))
        {
            return "INTEGER";
        }

        if (type.Contains("CHAR") || type.Contains("CLOB") || type.Contains("TEXT"))
        {
            return "TEXT";
        }

        if (type.Length == 0 || type.Contains("BLOB"))
        {
            return "BLOB";
        }

        if (type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB"))
        {
            return "REAL";
        }

        return "NUMERIC";
    }

    private static bool SetEquals(IEnumerable<string> a, IEnumerable<string> b)
        => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase)
            .SetEquals(new HashSet<string>(b, StringComparer.OrdinalIgnoreCase));

    private static bool IsInternalTable(string name)
        => name.StartsWith("__", StringComparison.Ordinal)
           || name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase);

    // ----------------- Structures internes -----------------

    private sealed record ExpectedTable(
        string Name,
        IReadOnlyList<ExpectedColumn> Columns,
        IReadOnlyList<string> PrimaryKeyColumns,
        IReadOnlyList<ExpectedForeignKey> ForeignKeys,
        IReadOnlyList<IReadOnlyList<string>> UniqueIndexes);

    private sealed record ExpectedColumn(string Name, string StoreType, bool IsNullable);

    private sealed record ExpectedForeignKey(IReadOnlyList<string> Columns, string PrincipalTable);

    private sealed record ActualTable(
        Dictionary<string, ActualColumn> Columns,
        IReadOnlyList<string> PrimaryKeyColumns,
        IReadOnlyList<ActualForeignKey> ForeignKeys,
        IReadOnlyList<IReadOnlyList<string>> UniqueIndexes);

    private sealed record ActualColumn(string DeclaredType, bool IsNullable);

    private sealed record ActualForeignKey(IReadOnlyList<string> Columns, string PrincipalTable);
}
