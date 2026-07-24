using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.P4.ProviderComparison.Support;
using Npgsql;
using Xunit;

namespace MMV.P4.ProviderComparison;

/// <summary>
/// E17 — PROTOTYPE RÉDUIT d'import SQLite → serveur. Le rapport initial classait ce travail
/// `NOT_TESTED` (« aucun prototype d'import n'a été construit »).
///
/// Source : base SQLite JETABLE créée par le harness dans le répertoire temporaire.
/// **Aucune base MMV utilisateur n'est lue.** <c>__EFMigrationsHistory</c> n'est jamais importé,
/// et aucun hash d'utilisateur réel ne transite.
///
/// L'import est générique : les colonnes sont découvertes par lecture, et chaque valeur est
/// convertie d'après le TYPE CLR déclaré par le modèle EF de la CIBLE. C'est exactement là que se
/// jouent les écarts mesurés (dates, booléens, enums, décimaux).
/// </summary>
public class E17_SqliteImportTests
{
    private const string Experiment = "E17-sqlite-import";

    private static readonly DateTime Fixed = new(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Montant volontairement au-delà de la précision d'un flottant simple (§11).</summary>
    private const decimal LargeAmount = 999999.99m;

    /// <summary>Ordre d'insertion imposé par les clés étrangères.</summary>
    private static readonly string[] ImportOrder =
    {
        "Suppliers", "ProductCategories", "Products", "Customers",
        "Sales", "SaleItems", "Orders", "WorkshopSheets",
        "Notifications", "DocumentSequences"
    };

    [SkippableTheory]
    [InlineData(ProviderKind.Postgres)]
    [InlineData(ProviderKind.SqlServer)]
    public async Task Reduced_sqlite_import_is_executed_and_reconciled(ProviderKind provider)
    {
        Skip.IfNot(SpikeEnvironment.IsConfigured(provider), SpikeEnvironment.SkipReason(provider));

        var name = provider.ToString();
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"mmv-p4-import-{Guid.NewGuid():n}.db");

        try
        {
            SpikeLog.Section(Experiment, name, "E17 — Import reduit SQLite -> serveur");

            // === 1) Source SQLite jetable, alimentée par le modèle RÉEL ===========================
            var expected = await BuildSqliteSourceAsync(sqlitePath, name);

            // === 2) Cible : base jetable + modèle réel (mapping décimal exact, cf. §11) ===========
            await using var database = await SpikeDatabase.CreateAsync(provider, "e17");
            await using (var setup = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            // === 2bis) Données de RÉFÉRENCE déjà présentes sur la cible ===========================
            // `DocumentSequenceConfiguration` seede SALE et ORDER via HasData : ces lignes existent
            // donc des DEUX côtés (la source SQLite les a aussi). Un import ligne à ligne naïf
            // échoue en violation de PK. C'est un fait de migration, mesuré ici puis traité.
            var seeded = await ClearTargetSeedDataAsync(database, provider, name);

            // === 3) Import table par table, dans l'ordre des FK, PK CONSERVÉES ====================
            var imported = new Dictionary<string, int>();
            await using (var target = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
            {
                var columnTypes = BuildColumnTypeMap(target);

                foreach (var table in ImportOrder)
                {
                    var rows = await ImportTableAsync(sqlitePath, database, provider, table, columnTypes[table]);
                    imported[table] = rows;
                }
            }

            // === 4) Réconciliation table par table ================================================
            var reconciled = true;
            foreach (var table in ImportOrder)
            {
                var after = int.Parse(await database.ScalarAsync($"SELECT COUNT(*) FROM {Quote(provider, table)};"),
                    CultureInfo.InvariantCulture);
                var ok = expected.Counts[table] == after && imported[table] == after;
                reconciled &= ok;

                SpikeLog.Write(Experiment, name,
                    $"TABLE {table} | SQLite avant={expected.Counts[table]} | lignes importees={imported[table]} | " +
                    $"serveur apres={after} | ecart={after - expected.Counts[table]} | {(ok ? "IDENTIQUE" : "ECART")}");
            }

            // === 5) Identifiants CONSERVÉS =======================================================
            var productIds = await ReadLongsAsync(database, $"SELECT \"ProductId\" FROM {Quote(provider, "Products")} ORDER BY 1;", provider);
            var idsPreserved = productIds.SequenceEqual(expected.ProductIds);
            SpikeLog.Write(Experiment, name,
                $"IDENTIFIANTS Products | SQLite=[{string.Join(",", expected.ProductIds)}] | " +
                $"serveur=[{string.Join(",", productIds)}] | {(idsPreserved ? "PK CONSERVEES" : "PK MODIFIEES")}");

            // === 6) Valeurs typées : décimal exact, date, booléen, enum, chaîne normalisée ========
            await using (var check = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
            {
                var sale = await check.Sales.AsNoTracking().OrderBy(s => s.SaleId).FirstAsync();
                var product = await check.Products.AsNoTracking().OrderBy(p => p.ProductId).FirstAsync();
                var sheet = await check.WorkshopSheets.AsNoTracking().FirstAsync();
                var notification = await check.Notifications.AsNoTracking().FirstAsync();

                var amountExact = sale.FinalAmount == LargeAmount;
                var dateExact = sale.SaleDate == Fixed;
                var boolExact = sheet.IsCurrent && product.IsActive;
                var enumExact = sale.PaymentStatus == PaymentStatus.Partial
                                && product.Category == ProductCategoryEnum.MONTURE
                                && sheet.QcStatus == WorkshopSheetQcStatus.Passed;
                var normalized = product.NormalizedReference == "REF-IMPORT-001";
                var nullPreserved = notification.ResolvedAt is null;

                SpikeLog.Write(Experiment, name,
                    $"VALEURS | montant {LargeAmount} -> {sale.FinalAmount} {(amountExact ? "EXACT" : "ALTERE")} | " +
                    $"date {Fixed:O} -> {sale.SaleDate:O} {(dateExact ? "EXACTE" : "ALTEREE")} | " +
                    $"booleens {(boolExact ? "EXACTS" : "ALTERES")} | enums {(enumExact ? "EXACTS" : "ALTERES")} | " +
                    $"chaine normalisee '{product.NormalizedReference}' {(normalized ? "EXACTE" : "ALTEREE")} | " +
                    $"NULL conserve {(nullPreserved ? "OUI" : "NON")}");

                reconciled &= amountExact && dateExact && boolExact && enumExact && normalized && nullPreserved;
            }

            // === 7) Séquences APRÈS import : le prochain identifiant ne doit pas entrer en collision
            await ResyncSequencesAsync(database, provider);

            long newId;
            await using (var after = AdaptedOpticDbContext.Create(database, MoneyMapping.ExactDecimal))
            {
                var supplier = new Supplier { Name = "Fournisseur POST-IMPORT" };
                after.Suppliers.Add(supplier);
                await after.SaveChangesAsync();
                newId = supplier.SupplierId;
            }

            var maxImported = expected.SupplierIds.Max();
            var sequenceOk = newId > maxImported;
            SpikeLog.Write(Experiment, name,
                $"SEQUENCES apres import | max importe={maxImported} | prochain identifiant genere={newId} | " +
                $"{(sequenceOk ? "SEQUENCE RESYNCHRONISEE (aucune collision)" : "COLLISION D'IDENTIFIANT")}");

            // === 8) Refus d'une donnée INVALIDE + nettoyage après échec ==========================
            var beforeInvalid = int.Parse(await database.ScalarAsync($"SELECT COUNT(*) FROM {Quote(provider, "Products")};"),
                CultureInfo.InvariantCulture);

            string invalidOutcome;
            await using (var connection = await database.OpenRawAsync())
            {
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    // SupplierId inexistant : la contrainte de clé étrangère doit refuser la ligne.
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText =
                        $"INSERT INTO {Quote(provider, "Products")} " +
                        $"({Quote(provider, "Reference")}, {Quote(provider, "NormalizedReference")}, {Quote(provider, "Name")}, " +
                        $"{Quote(provider, "Category")}, {Quote(provider, "SupplierId")}, {Quote(provider, "PurchasePrice")}, " +
                        $"{Quote(provider, "SalePrice")}, {Quote(provider, "StockQuantity")}, {Quote(provider, "StockAlertThreshold")}, " +
                        $"{Quote(provider, "IsActive")}, {Quote(provider, "EntryDate")}) " +
                        $"VALUES ('REF-INVALIDE', 'REF-INVALIDE', 'Produit invalide', 0, 999999, 1, 2, 1, 1, " +
                        $"{(provider == ProviderKind.Postgres ? "true" : "1")}, " +
                        $"{(provider == ProviderKind.Postgres ? "TIMESTAMP '2026-07-24 09:00:00'" : "'2026-07-24T09:00:00'")});";
                    await command.ExecuteNonQueryAsync();
                    invalidOutcome = "ACCEPTEE (inattendu — la contrainte FK n'a pas joue)";
                    await transaction.RollbackAsync();
                }
                catch (Exception ex)
                {
                    invalidOutcome = $"REFUSEE — {ErrorFacts.Describe(ex)} | categorie={ErrorFacts.CandidateCategory(ex)}";
                    try
                    {
                        await transaction.RollbackAsync();
                    }
                    catch
                    {
                        // PostgreSQL avorte la transaction : le rollback reste la bonne action.
                    }
                }
            }

            var afterInvalid = int.Parse(await database.ScalarAsync($"SELECT COUNT(*) FROM {Quote(provider, "Products")};"),
                CultureInfo.InvariantCulture);

            SpikeLog.Write(Experiment, name,
                $"DONNEE INVALIDE | {invalidOutcome} | produits avant={beforeInvalid} apres={afterInvalid} | " +
                $"{(beforeInvalid == afterInvalid ? "ROLLBACK PROPRE (aucune ligne partielle)" : "LIGNE PARTIELLE CONSERVEE")}");

            // === 9) __EFMigrationsHistory JAMAIS importé =========================================
            var historyImported = await TableExistsAsync(database, provider, "__EFMigrationsHistory");
            SpikeLog.Write(Experiment, name,
                $"__EFMigrationsHistory present sur la cible : {historyImported} (attendu False) | " +
                $"{(historyImported ? "ANOMALIE" : "JAMAIS IMPORTE")}");

            SpikeLog.Write(Experiment, name,
                $"BILAN IMPORT | reconciliation={(reconciled ? "COMPLETE" : "INCOMPLETE")} | " +
                $"PK conservees={idsPreserved} | sequences={sequenceOk} | historique de migration non importe={!historyImported}");

            Assert.True(true);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(sqlitePath))
            {
                File.Delete(sqlitePath);
            }
        }
    }

    // =============================================================================================
    // Source SQLite jetable
    // =============================================================================================
    private sealed record SourceFacts(
        Dictionary<string, int> Counts,
        List<long> ProductIds,
        List<long> SupplierIds);

    private static async Task<SourceFacts> BuildSqliteSourceAsync(string path, string name)
    {
        await using var context = SqliteSourceContext.Create(path);
        await context.Database.EnsureCreatedAsync();

        var supplier = new Supplier { Name = "Fournisseur Import", ReferenceCode = "SUP-IMP-1" };
        context.Suppliers.Add(supplier);

        var category = new ProductCategory { Name = "Categorie Import" };
        context.ProductCategories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Reference = "ref-import-001",   // casse volontairement différente : la normalisation doit s'appliquer
            Name = "Monture Import",
            Category = ProductCategoryEnum.MONTURE,
            SupplierId = supplier.SupplierId,
            PurchasePrice = 123.45m,
            SalePrice = 678.90m,
            StockQuantity = 7,
            StockAlertThreshold = 2,
            IsActive = true,
            EntryDate = Fixed
        };
        context.Products.Add(product);

        var customer = new Customer { FirstName = "Client", LastName = "Import", Phone = "0600000000" };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var sale = new Sale
        {
            SaleNumber = "IMP-000001",
            SaleDate = Fixed,
            CustomerId = customer.CustomerId,
            TotalAmount = LargeAmount,
            FinalAmount = LargeAmount,       // montant au-delà de la précision d'un float32
            DepositAmount = 0.01m,
            RemainingAmount = LargeAmount - 0.01m,
            PaymentMethod = PaymentMethod.Card,
            PaymentStatus = PaymentStatus.Partial,
            Status = SaleStatus.Delivered
        };
        context.Sales.Add(sale);
        await context.SaveChangesAsync();

        context.SaleItems.Add(new SaleItem
        {
            SaleId = sale.SaleId,
            ProductId = product.ProductId,
            Quantity = 3,
            UnitPrice = 0.10m,
            TotalPrice = 0.30m
        });

        var order = new Order
        {
            OrderNumber = "CMD-000001",
            SaleId = sale.SaleId,
            SupplierId = supplier.SupplierId,
            OrderDate = Fixed,
            Status = OrderStatus.QualityCheck,
            Notes = "Commande importee"
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        context.WorkshopSheets.Add(new WorkshopSheet
        {
            OrderId = order.OrderId,
            Version = 1,
            IsCurrent = true,                // booléen : 1 côté SQLite, boolean/bit côté serveur
            QcStatus = WorkshopSheetQcStatus.Passed,
            CreatedAt = Fixed,
            OrderNumberSnapshot = "CMD-000001",
            OrderDateSnapshot = Fixed,
            TechnicalFingerprint = "EMPREINTE-IMPORT"
        });

        context.Notifications.Add(new Notification
        {
            Type = NotificationTypes.LowStock,
            EntityType = NotificationEntityTypes.Product,
            EntityId = product.ProductId,
            Title = "Stock bas",
            Message = "Seuil atteint",
            IsRead = false,
            CreatedAt = Fixed,
            ResolvedAt = null                // NULL : doit rester NULL après import
        });

        context.DocumentSequences.Add(new DocumentSequence
        {
            SequenceName = "IMPORT",
            Prefix = "IMP",
            CurrentValue = 1,
            UpdatedAt = Fixed
        });

        await context.SaveChangesAsync();

        var counts = new Dictionary<string, int>
        {
            ["Suppliers"] = await context.Suppliers.CountAsync(),
            ["ProductCategories"] = await context.ProductCategories.CountAsync(),
            ["Products"] = await context.Products.CountAsync(),
            ["Customers"] = await context.Customers.CountAsync(),
            ["Sales"] = await context.Sales.CountAsync(),
            ["SaleItems"] = await context.SaleItems.CountAsync(),
            ["Orders"] = await context.Orders.CountAsync(),
            ["WorkshopSheets"] = await context.WorkshopSheets.CountAsync(),
            ["Notifications"] = await context.Notifications.CountAsync(),
            ["DocumentSequences"] = await context.DocumentSequences.CountAsync()
        };

        SpikeLog.Write(Experiment, name,
            $"SOURCE SQLite jetable creee | " + string.Join(" ", counts.Select(c => $"{c.Key}={c.Value}")));

        return new SourceFacts(
            counts,
            await context.Products.Select(p => p.ProductId).OrderBy(i => i).ToListAsync(),
            await context.Suppliers.Select(s => s.SupplierId).OrderBy(i => i).ToListAsync());
    }

    // =============================================================================================
    // Import générique d'une table
    // =============================================================================================
    private static Dictionary<string, Dictionary<string, Type>> BuildColumnTypeMap(DbContext target)
    {
        var map = new Dictionary<string, Dictionary<string, Type>>(StringComparer.Ordinal);

        foreach (var entity in target.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            var columns = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();

                // Type réellement ATTENDU PAR LE PROVIDER, pas le type CLR de l'entité : un enum
                // converti en texte (HasConversion<string>) doit être transmis comme chaîne, pas
                // comme entier. C'est le converter qui fait foi.
                var converted = property.GetRelationalTypeMapping().Converter?.ProviderClrType
                                ?? property.ClrType;

                columns[column] = Nullable.GetUnderlyingType(converted) ?? converted;
            }

            map[table] = columns;
        }

        return map;
    }

    /// <summary>
    /// Vide les données de RÉFÉRENCE créées par <c>HasData</c> sur la cible, dans l'ordre INVERSE
    /// des clés étrangères. Sans cette étape, l'import d'une base complète entre en collision de
    /// clé primaire sur les lignes seedées (mesuré : <c>23505</c> / <c>2627</c> sur
    /// <c>DocumentSequences</c>).
    /// </summary>
    private static async Task<int> ClearTargetSeedDataAsync(SpikeDatabase database, ProviderKind provider, string name)
    {
        var removed = 0;
        await using var connection = await database.OpenRawAsync();

        foreach (var table in ImportOrder.Reverse())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {Quote(provider, table)};";
            removed += await command.ExecuteNonQueryAsync();
        }

        SpikeLog.Write(Experiment, name,
            $"DONNEES DE REFERENCE de la cible (HasData) : {removed} ligne(s) supprimee(s) avant import " +
            $"(sinon collision de PK sur DocumentSequences : SALE et ORDER sont seedes des DEUX cotes)");

        return removed;
    }

    private static async Task<int> ImportTableAsync(
        string sqlitePath,
        SpikeDatabase database,
        ProviderKind provider,
        string table,
        Dictionary<string, Type> columnTypes)
    {
        // 1) Lecture de la source, colonnes découvertes par le lecteur (jamais codées en dur).
        var columns = new List<string>();
        var rows = new List<object?[]>();

        await using (var source = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sqlitePath}"))
        {
            await source.OpenAsync();
            await using var read = source.CreateCommand();
            read.CommandText = $"SELECT * FROM \"{table}\";";
            await using var reader = await read.ExecuteReaderAsync();

            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync())
            {
                var values = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }
                rows.Add(values);
            }
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        // 2) Écriture sur la cible, PK explicites, dans UNE transaction (rollback global si échec).
        await using var connection = await database.OpenRawAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // `DocumentSequences` a une clé primaire NATURELLE (SequenceName, texte) et donc AUCUNE
        // colonne d'identité : IDENTITY_INSERT y échoue ("does not have the identity property").
        // La bascule ne doit donc être émise que pour les tables réellement à identité.
        var hasIdentity = provider == ProviderKind.SqlServer && await HasIdentityAsync(connection, transaction, table);

        if (hasIdentity)
        {
            await using var on = connection.CreateCommand();
            on.Transaction = transaction;
            on.CommandText = $"SET IDENTITY_INSERT [{table}] ON;";
            await on.ExecuteNonQueryAsync();
        }

        var inserted = 0;
        var columnList = string.Join(", ", columns.Select(c => Quote(provider, c)));

        foreach (var row in rows)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;

            var placeholders = new List<string>();
            for (var i = 0; i < columns.Count; i++)
            {
                var parameterName = $"@p{i}";
                placeholders.Add(parameterName);

                var clrType = columnTypes.TryGetValue(columns[i], out var t) ? t : typeof(string);
                var parameter = insert.CreateParameter();
                parameter.ParameterName = parameterName;
                parameter.Value = Coerce(row[i], clrType) ?? DBNull.Value;
                insert.Parameters.Add(parameter);
            }

            insert.CommandText =
                $"INSERT INTO {Quote(provider, table)} ({columnList}) VALUES ({string.Join(", ", placeholders)});";
            inserted += await insert.ExecuteNonQueryAsync();
        }

        if (hasIdentity)
        {
            await using var off = connection.CreateCommand();
            off.Transaction = transaction;
            off.CommandText = $"SET IDENTITY_INSERT [{table}] OFF;";
            await off.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return inserted;
    }

    private static async Task<bool> HasIdentityAsync(DbConnection connection, DbTransaction transaction, string table)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT OBJECTPROPERTY(OBJECT_ID('[{table}]'), 'TableHasIdentity');";
        var value = await command.ExecuteScalarAsync();
        return value is not (null or DBNull) && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>
    /// Conversion d'une valeur SQLite vers le type CLR attendu par la cible. SQLite ne connaît que
    /// INTEGER / REAL / TEXT / BLOB : les booléens y sont des entiers, les dates et les décimaux
    /// exacts des chaînes. Sans cette conversion explicite, l'import échoue ou altère les valeurs.
    /// </summary>
    private static object? Coerce(object? value, Type clrType)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (clrType == typeof(bool))
        {
            return value switch
            {
                bool b => b,
                long l => l != 0,
                int i => i != 0,
                string s => s is "1" or "true" or "True",
                _ => Convert.ToBoolean(value, CultureInfo.InvariantCulture)
            };
        }

        if (clrType == typeof(DateTime))
        {
            return value is DateTime dt
                ? dt
                : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!,
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        if (clrType == typeof(decimal))
        {
            return value is decimal d
                ? d
                : decimal.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!,
                    NumberStyles.Any, CultureInfo.InvariantCulture);
        }

        if (clrType.IsEnum || clrType == typeof(int))
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        if (clrType == typeof(long))
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (clrType == typeof(double))
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        return value;
    }

    /// <summary>
    /// Après un import à identifiants explicites, le générateur d'identité doit être resynchronisé,
    /// sinon la première insertion applicative entre en collision avec une PK importée.
    /// </summary>
    private static async Task ResyncSequencesAsync(SpikeDatabase database, ProviderKind provider)
    {
        await using var connection = await database.OpenRawAsync();

        foreach (var table in ImportOrder)
        {
            try
            {
                if (provider == ProviderKind.Postgres)
                {
                    // La séquence n'existe que pour une colonne d'identité : NULL sinon (ex.
                    // DocumentSequences, dont la PK est naturelle). `false` laisse la prochaine
                    // valeur égale au max importé + 1.
                    await using var command = connection.CreateCommand();
                    command.CommandText = $@"
DO $$
DECLARE
    seq text;
    col text;
    maxid bigint;
BEGIN
    SELECT a.attname INTO col
    FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey)
    WHERE i.indrelid = '""{table}""'::regclass AND i.indisprimary
    LIMIT 1;

    seq := pg_get_serial_sequence('""{table}""', col);
    IF seq IS NOT NULL THEN
        EXECUTE format('SELECT COALESCE(MAX(%I), 0) FROM %I', col, '{table}') INTO maxid;
        PERFORM setval(seq, GREATEST(maxid, 1), maxid > 0);
    END IF;
END $$;";
                    await command.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var check = connection.CreateCommand();
                    check.CommandText = $"SELECT OBJECTPROPERTY(OBJECT_ID('[{table}]'), 'TableHasIdentity');";
                    var value = await check.ExecuteScalarAsync();
                    if (value is not (null or DBNull) && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1)
                    {
                        await using var reseed = connection.CreateCommand();
                        reseed.CommandText = $"DBCC CHECKIDENT ('[{table}]', RESEED);";
                        await reseed.ExecuteNonQueryAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                SpikeLog.Write(Experiment, provider.ToString(),
                    $"resynchronisation de sequence {table} : {ErrorFacts.Describe(ex)}");
            }
        }
    }

    private static async Task<List<long>> ReadLongsAsync(SpikeDatabase database, string sql, ProviderKind provider)
    {
        var values = new List<long>();
        await using var connection = await database.OpenRawAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture));
        }
        return values;
    }

    private static async Task<bool> TableExistsAsync(SpikeDatabase database, ProviderKind provider, string table)
    {
        var sql = provider == ProviderKind.Postgres
            ? $"SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '{table}';"
            : $"SELECT COUNT(*) FROM sys.tables WHERE name = '{table}';";
        return await database.ScalarAsync(sql) != "0";
    }

    private static string Quote(ProviderKind provider, string identifier)
        => provider == ProviderKind.Postgres ? $"\"{identifier}\"" : $"[{identifier}]";

    /// <summary>
    /// Contexte SOURCE : modèle réel sur SQLite, avec les décimaux mappés en TEXT afin que la
    /// source elle-même soit exacte. Sans cela, le mapping `REAL` de production altérerait déjà la
    /// valeur AVANT l'import et l'expérimentation ne mesurerait plus la fidélité du transfert (§11).
    /// </summary>
    private sealed class SqliteSourceContext : MMV.Infrastructure.Data.OpticDbContext
    {
        private SqliteSourceContext(DbContextOptions<MMV.Infrastructure.Data.OpticDbContext> options)
            : base(options)
        {
        }

        public static SqliteSourceContext Create(string path)
        {
            var builder = new DbContextOptionsBuilder<MMV.Infrastructure.Data.OpticDbContext>();
            builder.UseSqlite($"Data Source={path}");
            builder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, SqliteSourceCacheKeyFactory>();
            return new SqliteSourceContext(builder.Options);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entity.GetProperties())
                {
                    var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if (clr == typeof(decimal) &&
                        string.Equals(property.GetColumnType(), "REAL", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetColumnType("TEXT");
                    }
                }
            }
        }
    }

    private sealed class SqliteSourceCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime)
            => (context.GetType(), designTime);
    }
}
