using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P3-6B — Migration <c>AddWorkshopSheets</c> : additive, sans backfill, et physiquement conforme au modèle.
///
/// Exécutée sur une <b>vraie base SQLite jetable</b> (jamais le provider InMemory), via le pipeline de migrations
/// réel (<c>Database.Migrate()</c>), afin de prouver le schéma effectivement produit — pas seulement le modèle EF.
/// </summary>
public sealed class WorkshopSheetsMigrationTests : IDisposable
{
    private readonly string _workDirectory;

    public WorkshopSheetsMigrationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p36b-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }
        }
        catch
        {
            // Nettoyage best-effort.
        }
    }

    private const string WorkshopSheetsTable = "WorkshopSheets";
    private const string WorkshopSheetItemsTable = "WorkshopSheetItems";
    private const string MigrationSuffix = "_AddWorkshopSheets";

    private OpticDbContext CreateContext(string fileName = "migrate.db")
    {
        var path = Path.Combine(_workDirectory, fileName);
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static List<string> Query(OpticDbContext context, string sql)
    {
        var values = new List<string>();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            command.Connection.Open();
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.IsDBNull(0) ? string.Empty : reader.GetValue(0).ToString() ?? string.Empty);
        }

        return values;
    }

    private static List<(string Name, int NotNull)> Columns(OpticDbContext context, string table)
    {
        var columns = new List<(string, int)>();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            command.Connection.Open();
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add((reader.GetString(1), reader.GetInt32(3)));
        }

        return columns;
    }

    private static List<(string Table, string OnDelete)> ForeignKeys(OpticDbContext context, string table)
    {
        var keys = new List<(string, string)>();
        using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list('{table}');";

        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            command.Connection.Open();
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            keys.Add((reader.GetString(2), reader.GetString(6)));
        }

        return keys;
    }

    // -------------------------------------------------------------------------------------------------------
    // Migration complète depuis zéro
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Migrate_DepuisZero_CreeLesDeuxTables()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var tables = Query(context, "SELECT name FROM sqlite_master WHERE type='table';");

        tables.Should().Contain(WorkshopSheetsTable);
        tables.Should().Contain(WorkshopSheetItemsTable);
    }

    [Fact]
    public void Migrate_LaMigrationEstInscriteDansLHistorique()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.Database.GetAppliedMigrations()
            .Should().Contain(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public void Migrate_CreeLesColonnesAttenduesSurLaTableRacine()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var columns = Columns(context, WorkshopSheetsTable).Select(c => c.Name).ToList();

        columns.Should().Contain(new[]
        {
            "WorkshopSheetId", "OrderId", "Version", "IsCurrent", "CreatedAt", "TechnicalFingerprint",
            "OrderNumberSnapshot", "OrderDateSnapshot", "EstimatedDeliverySnapshot", "CustomerNameSnapshot",
            "InstructionsSnapshot", "QcStatus", "QcComment", "QcCompletedAt"
        });
    }

    [Fact]
    public void Migrate_LaTableRacine_NePorteAucuneDonneePersonnelleAuDelaDuNom()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var columns = Columns(context, WorkshopSheetsTable).Select(c => c.Name).ToList();

        columns.Should().NotContain(c => c.Contains("Phone", StringComparison.OrdinalIgnoreCase));
        columns.Should().NotContain(c => c.Contains("Email", StringComparison.OrdinalIgnoreCase));
        columns.Should().NotContain(c => c.Contains("Address", StringComparison.OrdinalIgnoreCase));
        columns.Should().NotContain(c => c.Contains("Amount", StringComparison.OrdinalIgnoreCase));
        columns.Should().NotContain(c => c.Contains("Deposit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Migrate_CreeLesColonnesOptiquesSourceEtTransposeeSurLesLignes()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var columns = Columns(context, WorkshopSheetItemsTable).Select(c => c.Name).ToList();

        // Les deux notations coexistent : la source n'est jamais remplacée.
        columns.Should().Contain(new[] { "SourceSphere", "SourceCylinder", "SourceAxis" });
        columns.Should().Contain(new[] { "TransposedSphere", "TransposedCylinder", "TransposedAxis", "HasTransposition" });

        // Champs hors portée de la transposition, indispensables au bon d'atelier.
        columns.Should().Contain(new[] { "UsageType", "Addition", "PrismValue", "PrismBase", "VisualAcuity" });

        // Snapshot produit par valeur.
        columns.Should().Contain(new[]
        {
            "SourceProductId", "ProductReferenceSnapshot", "ProductNameSnapshot", "ProductCategorySnapshot"
        });
    }

    // -------------------------------------------------------------------------------------------------------
    // Index et contraintes
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Migrate_CreeLIndexUniqueSurLeCoupleCommandeVersion()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var indexes = Query(context,
            $"SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='{WorkshopSheetsTable}';");

        indexes.Should().Contain("idx_workshop_sheets_order_version_unique");
    }

    [Fact]
    public void Migrate_CreeLIndexUniqueFiltreDeLaVersionCourante()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var definition = Query(context,
            $"SELECT sql FROM sqlite_master WHERE type='index' AND name='idx_workshop_sheets_current_unique';");

        definition.Should().ContainSingle();
        definition[0].Should().Contain("UNIQUE");
        definition[0].Should().Contain("WHERE");
        definition[0].Should().Contain("IsCurrent");
    }

    [Fact]
    public void Migrate_LaCleEtrangereVersOrder_EstEnRestrict()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var foreignKeys = ForeignKeys(context, WorkshopSheetsTable);

        var orderFk = foreignKeys.Should().ContainSingle(fk => fk.Table == "Orders").Subject;
        orderFk.OnDelete.Should().Be("RESTRICT", "une fiche historique ne doit jamais être effacée en cascade");
    }

    [Fact]
    public void Migrate_LaCleEtrangereDesLignesVersLaFiche_EstEnCascade()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var foreignKeys = ForeignKeys(context, WorkshopSheetItemsTable);

        var sheetFk = foreignKeys.Should().ContainSingle(fk => fk.Table == WorkshopSheetsTable).Subject;
        sheetFk.OnDelete.Should().Be("CASCADE", "cascade autorisée uniquement de la racine d'agrégat vers ses lignes");
    }

    [Fact]
    public void Migrate_LesLignesNOntAucuneCleEtrangereVersLeCatalogueOuLeClient()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var referencedTables = ForeignKeys(context, WorkshopSheetItemsTable).Select(fk => fk.Table).ToList();

        // Le snapshot doit survivre à un renommage/une désactivation du catalogue : aucune FK vers Products.
        referencedTables.Should().NotContain("Products");
        referencedTables.Should().NotContain("Customers");
        referencedTables.Should().NotContain("Prescriptions");
        referencedTables.Should().OnlyContain(t => t == WorkshopSheetsTable);
    }

    // -------------------------------------------------------------------------------------------------------
    // Caractère additif : aucun backfill, aucune table métier modifiée
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Migrate_NeCreeAucuneFicheNiAucunControleQualite()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.WorkshopSheets.Should().BeEmpty("aucun backfill : une base antérieure n'a simplement aucune fiche");
        context.WorkshopSheetItems.Should().BeEmpty();
    }

    [Fact]
    public void Migrate_NeModifieAucunStatutDeCommandeExistant()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        // Aucune commande n'est créée par la migration, et aucune instruction UPDATE sur Orders n'y figure.
        context.Orders.Should().BeEmpty();
    }

    [Fact]
    public void MigrationAddWorkshopSheets_NeToucheQueLesTablesDeFicheAtelier()
    {
        // Lecture du SQL réellement généré pour cette seule migration : il ne doit contenir aucune instruction
        // portant sur une table métier existante (pas d'ALTER/DROP/UPDATE/INSERT ailleurs).
        using var context = CreateContext("sqlscript.db");

        var migrations = context.Database.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        var index = migrations.FindIndex(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
        index.Should().BeGreaterThan(0, "la migration P3-6B doit exister et ne pas être la première");

        var previous = migrations[index - 1];
        var target = migrations[index];

        var generator = context.Database.GetService<IMigrator>();
        var script = generator.GenerateScript(previous, target);

        script.Should().Contain(WorkshopSheetsTable);
        script.Should().Contain(WorkshopSheetItemsTable);

        // Aucune table métier existante n'est touchée par cette migration.
        foreach (var protectedTable in new[] { "Customers", "Products", "Sales", "SaleItems", "Prescriptions", "StockMovements" })
        {
            script.Should().NotContain($"ALTER TABLE \"{protectedTable}\"");
            script.Should().NotContain($"DROP TABLE \"{protectedTable}\"");
            script.Should().NotContain($"UPDATE \"{protectedTable}\"");
            script.Should().NotContain($"INSERT INTO \"{protectedTable}\"");
        }

        // Orders n'est référencée que comme cible de clé étrangère, jamais modifiée.
        script.Should().NotContain("ALTER TABLE \"Orders\"");
        script.Should().NotContain("UPDATE \"Orders\"");
    }

    // -------------------------------------------------------------------------------------------------------
    // Rollback
    // -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Down_SupprimeLesDeuxTables_SansToucherLeResteDuSchema()
    {
        using var context = CreateContext("rollback.db");
        context.Database.Migrate();

        var migrations = context.Database.GetService<IMigrationsAssembly>().Migrations.Keys.ToList();
        var index = migrations.FindIndex(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
        var previous = migrations[index - 1];

        context.Database.GetService<IMigrator>().Migrate(previous);

        var tables = Query(context, "SELECT name FROM sqlite_master WHERE type='table';");

        tables.Should().NotContain(WorkshopSheetsTable);
        tables.Should().NotContain(WorkshopSheetItemsTable);

        // Les tables métier survivent intactes : la migration est bien purement additive.
        tables.Should().Contain(new[] { "Orders", "OrderItems", "Customers", "Products", "Sales" });
    }
}
