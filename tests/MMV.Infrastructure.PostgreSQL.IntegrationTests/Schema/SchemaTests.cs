using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Schema;

/// <summary>
/// N1 (ADR-005 G5, ADR-007 S4) et N2 (ADR-006 X5, ADR-007 S3, ADR-003 M4) : le schéma réellement créé par
/// la chaîne de migrations PostgreSQL, lu dans le catalogue du serveur.
/// </summary>
public class SchemaTests : PostgreSqlTestBase
{
    // ---------- N1 ----------

    [PostgreSqlFact]
    public async Task N1_two_freshDatabases_migrated_independently_have_identical_schemas()
    {
        await using var second = await PostgreSqlDatabase.CreateAsync();

        var first = await SchemaCatalog.FingerprintAsync(Database);
        var other = await SchemaCatalog.FingerprintAsync(second);

        first.Should().NotBeEmpty();
        other.Should().Equal(first);
    }

    [PostgreSqlFact]
    public async Task N1_migrating_an_up_to_dateDatabase_again_changes_nothing()
    {
        var before = await SchemaCatalog.FingerprintAsync(Database);

        await using (var context = Database.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        (await SchemaCatalog.FingerprintAsync(Database)).Should().Equal(before);
    }

    // ---------- N2 : confrontation physique au modèle EF ----------

    [PostgreSqlFact]
    public async Task N2_physical_tables_are_exactly_the_model_tables_plus_migration_history()
    {
        var physical = await SchemaCatalog.QueryAsync(Database,
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'");

        physical.Should().BeEquivalentTo(Tables().Select(t => t.Name).Append("__EFMigrationsHistory"));
    }

    [PostgreSqlFact]
    public async Task N2_every_column_has_the_model_store_type_and_nullability()
    {
        var physical = await SchemaCatalog.QueryAsync(Database, """
            SELECT c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
                   || CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE ' NULL' END
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
              AND c.relname <> '__EFMigrationsHistory'
            """);

        // Les noms de type PostgreSQL ne sont pas sensibles à la casse : le modèle déclare parfois « TEXT ».
        var expected = Tables().SelectMany(t => t.Columns.Select(c =>
            $"{t.Name}.{c.Name} {c.StoreType.ToLowerInvariant()} {(c.IsNullable ? "NULL" : "NOT NULL")}"));

        physical.Should().BeEquivalentTo(expected);
    }

    [PostgreSqlFact]
    public async Task N2_the_14_money_columns_of_ADR_003_are_exactly_the_numeric_12_2_columns()
    {
        // ADR-PROD-DB-003 : 14 colonnes monétaires. Les autres décimaux (puissances, diamètres, indices
        // optiques) portent leur propre précision et ne doivent PAS être en numeric(12,2).
        var physical = await SchemaCatalog.QueryAsync(Database, """
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND data_type = 'numeric'
              AND numeric_precision = 12 AND numeric_scale = 2
            """);

        physical.Should().BeEquivalentTo(
            "GlassPricingTiers.PurchasePriceGrid", "GlassPricingTiers.SalePriceGrid",
            "OrderItems.UnitPrice",
            "Products.PurchasePrice", "Products.RecommendedPrice", "Products.SalePrice",
            "SaleItems.TotalPrice", "SaleItems.UnitPrice",
            "Sales.DepositAmount", "Sales.DiscountAmount", "Sales.FinalAmount", "Sales.RemainingAmount", "Sales.TotalAmount",
            "Supplements.SupplementPrice");
    }

    [PostgreSqlFact]
    public async Task N2_primary_unique_and_foreign_keys_match_the_model_including_on_delete()
    {
        var physical = await SchemaCatalog.QueryAsync(Database, """
            SELECT con.conrelid::regclass::text || ' ' || con.conname || ' ' || con.contype::text
                   || CASE WHEN con.contype = 'f' THEN ' ' || con.confdeltype::text ELSE '' END
            FROM pg_constraint con
            JOIN pg_namespace n ON n.oid = con.connamespace
            WHERE n.nspname = 'public' AND con.contype IN ('p', 'u', 'f')
              AND con.conrelid::regclass::text <> '"__EFMigrationsHistory"'
            """);

        var expected = Tables().SelectMany(t =>
            new[] { $"\"{t.Name}\" {t.PrimaryKey!.Name} p" }
                .Concat(t.UniqueConstraints.Where(u => !u.GetIsPrimaryKey()).Select(u => $"\"{t.Name}\" {u.Name} u"))
                .Concat(t.ForeignKeyConstraints.Select(fk => $"\"{t.Name}\" {fk.Name} f {DeleteCode(fk.OnDeleteAction)}")));

        physical.Should().BeEquivalentTo(expected);
    }

    [PostgreSqlFact]
    public async Task N2_indexes_match_the_model_in_name_uniqueness_and_presence_of_filter()
    {
        var physical = await SchemaCatalog.QueryAsync(Database, """
            SELECT t.relname || ' ' || i.relname || ' ' || CASE WHEN x.indisunique THEN 'unique' ELSE 'non-unique' END
                   || CASE WHEN x.indpred IS NOT NULL THEN ' filtered' ELSE '' END
            FROM pg_index x
            JOIN pg_class i ON i.oid = x.indexrelid
            JOIN pg_class t ON t.oid = x.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = 'public' AND NOT x.indisprimary AND t.relname <> '__EFMigrationsHistory'
            """);

        var expected = Tables().SelectMany(t => t.Indexes.Select(i =>
            $"{t.Name} {i.Name} {(i.IsUnique ? "unique" : "non-unique")}{(i.Filter is null ? "" : " filtered")}"));

        physical.Should().BeEquivalentTo(expected);
    }

    [PostgreSqlFact]
    public async Task N2_the_two_filtered_indexes_carry_their_exact_predicates()
    {
        var predicates = await SchemaCatalog.QueryAsync(Database, """
            SELECT i.relname || ' :: ' || pg_get_expr(x.indpred, x.indrelid)
            FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid
            WHERE x.indpred IS NOT NULL
            """);

        predicates.Should().BeEquivalentTo(
            "idx_workshop_sheets_current_unique :: \"IsCurrent\"",
            // Forme re-analysée par PostgreSQL : "Type" et "EntityType" étant des varchar, il explicite le cast ::text.
            "idx_notifications_active_low_stock_unique :: (((\"Type\")::text = 'LowStock'::text) AND ((\"EntityType\")::text = 'Product'::text) AND (\"EntityId\" IS NOT NULL) AND (\"ResolvedAt\" IS NULL))");
    }

    private IReadOnlyList<ITable> Tables()
    {
        using var context = Database.CreateContext();
        return context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables.ToList();
    }

    private static char DeleteCode(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => 'c',
        ReferentialAction.Restrict => 'r',
        ReferentialAction.NoAction => 'a',
        ReferentialAction.SetNull => 'n',
        ReferentialAction.SetDefault => 'd',
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}
