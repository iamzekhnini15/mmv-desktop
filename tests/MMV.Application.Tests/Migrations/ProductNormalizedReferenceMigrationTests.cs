using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Application.Tests.Migrations;

/// <summary>
/// P3-4B — Vérifie la migration <c>AddProductNormalizedReferenceAndProtectHistory</c> sur une base SQLite
/// <b>jetable</b> (jamais la base réelle).
/// <para>
/// Stratégie de backfill validée ici : <b>« exact ou échec sûr »</b>. Pour une référence purement ASCII
/// imprimable, <c>UPPER(TRIM(...))</c> de SQLite reproduit exactement <c>Trim().ToUpperInvariant()</c> ; pour
/// toute référence contenant un caractère hors ASCII imprimable (accents latins, alphabets grec/cyrillique…), la
/// migration <b>échoue de façon sûre</b> — aucune ligne supprimée, fusionnée ni modifiée, migration non
/// enregistrée — plutôt que d'écrire une valeur approximative. Sont aussi couverts : collision ASCII, conservation
/// des ventes/commandes/mouvements à travers la migration, index unique normalisé et FK <c>RESTRICT</c>.
/// </para>
/// </summary>
public sealed class ProductNormalizedReferenceMigrationTests : IDisposable
{
    private const string PreviousMigration = "20260713215132_AddCustomerArchivingAndProtectHistory";

    private readonly string _workDirectory;

    public ProductNormalizedReferenceMigrationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p34b-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_workDirectory)) Directory.Delete(_workDirectory, recursive: true); }
        catch { /* best-effort */ }
    }

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    private static void MigrateTo(string dbPath, string targetMigration)
    {
        using var ctx = CreateContext(dbPath);
        ctx.Database.GetService<IMigrator>().Migrate(targetMigration);
    }

    private static void MigrateToLatest(string dbPath)
    {
        using var ctx = CreateContext(dbPath);
        ctx.Database.Migrate();
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Insère un fournisseur puis des produits dans le schéma <b>précédent</b> (sans NormalizedReference).</summary>
    private static long SeedSupplierAndProductsAtPreviousSchema(string dbPath, params string[] references)
    {
        using var connection = OpenConnection(dbPath);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO \"Suppliers\" (\"Name\") VALUES ('F'); SELECT last_insert_rowid();";
        var supplierId = (long)cmd.ExecuteScalar()!;

        foreach (var reference in references)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText =
                "INSERT INTO \"Products\" (\"Reference\", \"Name\", \"Category\", \"SupplierId\", \"PurchasePrice\", \"SalePrice\", \"EntryDate\") " +
                "VALUES ($ref, $name, 'MONTURE', $sid, 1, 2, '2024-01-01 00:00:00');";
            insert.Parameters.AddWithValue("$ref", reference);
            insert.Parameters.AddWithValue("$name", "P " + reference);
            insert.Parameters.AddWithValue("$sid", supplierId);
            insert.ExecuteNonQuery();
        }

        return supplierId;
    }

    /// <summary>Confirme que la migration P3-4B n'a PAS été enregistrée (elle a échoué / n'a pas été appliquée).</summary>
    private static void AssertP34bMigrationNotRecorded(string dbPath)
    {
        using var connection = OpenConnection(dbPath);
        using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%AddProductNormalizedReferenceAndProtectHistory';";
        ((long)cmd.ExecuteScalar()!).Should().Be(0, "la migration doit rester non enregistrée après un échec sûr");
    }

    [Fact]
    public void FreshCreate_AppliesAllMigrations_AndNormalizationEnforced()
    {
        var dbPath = PathFor("fresh.db");
        MigrateToLatest(dbPath);

        using var ctx = CreateContext(dbPath);
        var supplier = new Supplier { Name = "F" };
        ctx.Suppliers.Add(supplier);
        ctx.SaveChanges();

        ctx.Products.Add(new Product { Reference = " abc-1 ", Name = "P", Category = ProductCategoryEnum.MONTURE, SupplierId = supplier.SupplierId, SalePrice = 2m });
        ctx.SaveChanges();

        ctx.Products.AsNoTracking().Single().NormalizedReference.Should().Be("ABC-1");
    }

    [Fact]
    public void Upgrade_BackfillsAsciiReference_ExactlyLikeToUpperInvariant_AndKeepsAllRows()
    {
        var dbPath = PathFor("upgrade-ascii.db");
        MigrateTo(dbPath, PreviousMigration);
        SeedSupplierAndProductsAtPreviousSchema(dbPath, "ABC-123", "xyz-9", " Ref 3 ");

        MigrateToLatest(dbPath);

        using var ctx = CreateContext(dbPath);
        var products = ctx.Products.AsNoTracking().OrderBy(p => p.ProductId).ToList();
        products.Should().HaveCount(3);
        // Références d'affichage inchangées ; représentation normalisée = EXACTEMENT Trim().ToUpperInvariant().
        products[0].Reference.Should().Be("ABC-123");
        products[0].NormalizedReference.Should().Be(Product.NormalizeReference("ABC-123")).And.Be("ABC-123");
        products[1].NormalizedReference.Should().Be(Product.NormalizeReference("xyz-9")).And.Be("XYZ-9");
        products[2].NormalizedReference.Should().Be(Product.NormalizeReference(" Ref 3 ")).And.Be("REF 3");
    }

    [Fact]
    public void Upgrade_WithAsciiCollidingReferences_Fails_WithoutDataLoss()
    {
        var dbPath = PathFor("collision-ascii.db");
        MigrateTo(dbPath, PreviousMigration);
        // "dup-1" et "DUP-1" convergent après normalisation → l'index unique doit refuser la migration.
        SeedSupplierAndProductsAtPreviousSchema(dbPath, "dup-1", "DUP-1");

        var act = () => MigrateToLatest(dbPath);
        act.Should().Throw<SqliteException>();

        // Aucune ligne détruite ni fusionnée ; migration non enregistrée.
        using (var connection = OpenConnection(dbPath))
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Products\";").Should().Be(2);
        AssertP34bMigrationNotRecorded(dbPath);
    }

    [Fact]
    public void Upgrade_WithAccentedReference_FailsSafely_WithoutDataLoss()
    {
        // Référence française accentuée héritée. SQLite ne peut pas reproduire ToUpperInvariant à l'identique pour
        // « é » : plutôt qu'un backfill approximatif, la garde avorte la migration. Aucune donnée modifiée.
        var dbPath = PathFor("accent.db");
        MigrateTo(dbPath, PreviousMigration);
        SeedSupplierAndProductsAtPreviousSchema(dbPath, " réf-é ");

        var act = () => MigrateToLatest(dbPath);
        act.Should().Throw<SqliteException>();

        using (var connection = OpenConnection(dbPath))
        {
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Products\";").Should().Be(1);
            // La référence d'affichage reste strictement intacte (aucune modification partielle).
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT \"Reference\" FROM \"Products\";";
            ((string)cmd.ExecuteScalar()!).Should().Be(" réf-é ");
        }
        AssertP34bMigrationNotRecorded(dbPath);
    }

    [Fact]
    public void Upgrade_WithGreekOrCyrillicReference_FailsSafely_WithoutDataLoss()
    {
        // Alphabets non latins : hors du champ que SQLite peut normaliser comme ToUpperInvariant → échec sûr.
        var dbPath = PathFor("greek-cyrillic.db");
        MigrateTo(dbPath, PreviousMigration);
        SeedSupplierAndProductsAtPreviousSchema(dbPath, "ωμέγα-1", "привет-2");

        var act = () => MigrateToLatest(dbPath);
        act.Should().Throw<SqliteException>();

        using (var connection = OpenConnection(dbPath))
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Products\";").Should().Be(2);
        AssertP34bMigrationNotRecorded(dbPath);
    }

    [Fact]
    public void Upgrade_WithAccentedCollidingReferences_FailsSafely_WithoutDataLoss()
    {
        // Collision accentuée : « réf-é » / « RÉF-É ». La garde non-ASCII avorte AVANT même l'index unique.
        // Aucune fusion, aucune perte : les deux lignes d'origine subsistent.
        var dbPath = PathFor("accent-collision.db");
        MigrateTo(dbPath, PreviousMigration);
        SeedSupplierAndProductsAtPreviousSchema(dbPath, "réf-é", "RÉF-É");

        var act = () => MigrateToLatest(dbPath);
        act.Should().Throw<SqliteException>();

        using (var connection = OpenConnection(dbPath))
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Products\";").Should().Be(2);
        AssertP34bMigrationNotRecorded(dbPath);
    }

    [Fact]
    public void Upgrade_PreservesSalesOrdersAndStockMovements_ThroughMigration()
    {
        // Migration réussie (références ASCII) avec conservation intégrale de l'historique dépendant :
        // une vente + ligne de vente, une commande + ligne de commande, un mouvement de stock, tous rattachés au
        // produit hérité, doivent survivre au drop/recreate des FK (désormais RESTRICT).
        var dbPath = PathFor("preserve-history.db");
        MigrateTo(dbPath, PreviousMigration);
        var supplierId = SeedSupplierAndProductsAtPreviousSchema(dbPath, "keep-1");

        using (var connection = OpenConnection(dbPath))
        {
            var productId = ScalarLong(connection, "SELECT \"ProductId\" FROM \"Products\";");

            Exec(connection,
                "INSERT INTO \"Sales\" (\"SaleNumber\", \"PaymentMethod\", \"SaleDate\", \"TotalAmount\", \"FinalAmount\") " +
                "VALUES ('S1', 'Cash', '2024-01-01 00:00:00', 2, 2);");
            var saleId = ScalarLong(connection, "SELECT last_insert_rowid();");
            Exec(connection,
                "INSERT INTO \"SaleItems\" (\"SaleId\", \"ProductId\", \"Quantity\", \"UnitPrice\", \"TotalPrice\", \"ItemType\") " +
                $"VALUES ({saleId}, {productId}, 1, 2, 2, 'Frame');");

            Exec(connection,
                $"INSERT INTO \"Orders\" (\"OrderNumber\", \"SaleId\", \"OrderDate\") VALUES ('O1', {saleId}, '2024-01-01 00:00:00');");
            var orderId = ScalarLong(connection, "SELECT last_insert_rowid();");
            Exec(connection,
                "INSERT INTO \"OrderItems\" (\"OrderId\", \"ProductId\", \"ItemType\", \"UnitPrice\") " +
                $"VALUES ({orderId}, {productId}, 'Frame', 2);");

            Exec(connection,
                "INSERT INTO \"StockMovements\" (\"ProductId\", \"MovementType\", \"Quantity\", \"CreatedAt\") " +
                $"VALUES ({productId}, 'In', 5, '2024-01-01 00:00:00');");
        }

        MigrateToLatest(dbPath);

        using (var connection = OpenConnection(dbPath))
        {
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Products\";").Should().Be(1);
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Sales\";").Should().Be(1);
            ScalarLong(connection, "SELECT COUNT(*) FROM \"SaleItems\";").Should().Be(1);
            ScalarLong(connection, "SELECT COUNT(*) FROM \"Orders\";").Should().Be(1);
            ScalarLong(connection, "SELECT COUNT(*) FROM \"OrderItems\";").Should().Be(1);
            ScalarLong(connection, "SELECT COUNT(*) FROM \"StockMovements\";").Should().Be(1);
        }

        using var ctx = CreateContext(dbPath);
        ctx.Products.AsNoTracking().Single().NormalizedReference.Should().Be("KEEP-1");
        _ = supplierId; // fournisseur également conservé (FK Products→Suppliers intacte).
    }

    [Fact]
    public void Upgrade_CreatesUniqueNormalizedIndex_AndDropsOldReferenceIndex()
    {
        var dbPath = PathFor("index.db");
        MigrateToLatest(dbPath);

        using var connection = OpenConnection(dbPath);

        // L'index unique porte exactement sur NormalizedReference.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                "SELECT \"unique\" FROM pragma_index_list('Products') WHERE \"name\" = 'idx_products_normalized_reference_unique';";
            cmd.ExecuteScalar().Should().NotBeNull();
            ((long)cmd.ExecuteScalar()!).Should().Be(1, "l'index normalisé doit être UNIQUE");
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                "SELECT \"name\" FROM pragma_index_info('idx_products_normalized_reference_unique');";
            ((string)cmd.ExecuteScalar()!).Should().Be("NormalizedReference");
        }
        // L'ancien index unique sur Reference a disparu.
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM pragma_index_list('Products') WHERE \"name\" = 'idx_products_reference_unique';";
            ((long)cmd.ExecuteScalar()!).Should().Be(0);
        }
    }

    [Fact]
    public void Upgrade_EnforcesRestrictOnProductDeletion_WhenReferencedBySaleItem()
    {
        var dbPath = PathFor("restrict.db");
        MigrateToLatest(dbPath);

        using var ctx = CreateContext(dbPath);
        var supplier = new Supplier { Name = "F" };
        ctx.Suppliers.Add(supplier);
        ctx.SaveChanges();

        var product = new Product { Reference = "ref-restrict", Name = "P", Category = ProductCategoryEnum.MONTURE, SupplierId = supplier.SupplierId, SalePrice = 2m };
        ctx.Products.Add(product);
        ctx.SaveChanges();

        using var connection = OpenConnection(dbPath);
        Exec(connection,
            "INSERT INTO \"Sales\" (\"SaleNumber\", \"PaymentMethod\", \"SaleDate\", \"TotalAmount\", \"FinalAmount\") " +
            "VALUES ('S1', 'Cash', '2024-01-01 00:00:00', 2, 2);");
        var saleId = ScalarLong(connection, "SELECT last_insert_rowid();");
        Exec(connection,
            "INSERT INTO \"SaleItems\" (\"SaleId\", \"ProductId\", \"Quantity\", \"UnitPrice\", \"TotalPrice\", \"ItemType\") " +
            $"VALUES ({saleId}, {product.ProductId}, 1, 2, 2, 'Frame');");

        // FK RESTRICT : la base doit refuser la suppression d'un produit référencé (aucun SET NULL / CASCADE).
        Exec(connection, "PRAGMA foreign_keys = ON;");
        using var del = connection.CreateCommand();
        del.CommandText = $"DELETE FROM \"Products\" WHERE \"ProductId\" = {product.ProductId};";
        var act = () => del.ExecuteNonQuery();
        act.Should().Throw<SqliteException>();

        ScalarLong(connection, "SELECT COUNT(*) FROM \"Products\";").Should().Be(1);
        ScalarLong(connection, "SELECT COUNT(*) FROM \"SaleItems\";").Should().Be(1);
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
