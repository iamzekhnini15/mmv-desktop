using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1B — diagnostic détaillé d'une base SQLite historique : fichier absent, fichier invalide,
/// base historique compatible/incompatible, base gérée par migrations, base vide. Lecture seule
/// vérifiée. Tous les tests utilisent des fichiers temporaires isolés (jamais la base réelle).
/// </summary>
public sealed class SqliteHistoricalDatabaseDiagnosticTests : IDisposable
{
    private readonly string _workDirectory;

    public SqliteHistoricalDatabaseDiagnosticTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-diag-" + Guid.NewGuid().ToString("N"));
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

    private string PathFor(string fileName) => Path.Combine(_workDirectory, fileName);

    private static OpticDbContext CreateContext(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options;
        return new OpticDbContext(options);
    }

    private static HistoricalDatabaseDiagnosticResult Diagnose(string databasePath)
    {
        using var context = CreateContext(databasePath);
        return new SqliteHistoricalDatabaseDiagnostic().Diagnose(context);
    }

    // ---------------------------------------------------------------------
    // 1) Aucun fichier de base
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_NoFile_ReturnsNoDatabaseFound_AndDoesNotCreateFile()
    {
        var dbPath = PathFor("absent.db");

        var result = Diagnose(dbPath);

        result.FileExists.Should().BeFalse();
        result.RecommendedDecision.Should().Be(HistoricalDatabaseDecision.NoDatabaseFound);
        File.Exists(dbPath).Should().BeFalse("le diagnostic est en lecture seule : aucun fichier ne doit être créé");
    }

    // ---------------------------------------------------------------------
    // 2) Fichier présent mais non-SQLite (corrompu)
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_InvalidFile_ReturnsRejectInvalidDatabase_AndPreservesBytes()
    {
        var dbPath = PathFor("corrupt.db");
        var garbage = new byte[] { 0x4D, 0x4D, 0x56, 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE };
        File.WriteAllBytes(dbPath, garbage);

        var result = Diagnose(dbPath);

        result.FileExists.Should().BeTrue();
        result.IsValidSqlite.Should().BeFalse();
        result.RecommendedDecision.Should().Be(HistoricalDatabaseDecision.RejectInvalidDatabase);
        result.FileSizeBytes.Should().Be(garbage.Length);
        File.ReadAllBytes(dbPath).Should().Equal(garbage, "le diagnostic ne modifie jamais la base");
    }

    // ---------------------------------------------------------------------
    // 3) Base SQLite valide mais vide
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_EmptyValidSqlite_ReturnsNoDatabaseFound()
    {
        var dbPath = PathFor("empty.db");
        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open(); // crée un fichier SQLite valide mais sans table applicative
        }
        SqliteConnection.ClearAllPools();

        var result = Diagnose(dbPath);

        result.IsValidSqlite.Should().BeTrue();
        result.HasApplicationTables.Should().BeFalse();
        result.RecommendedDecision.Should().Be(HistoricalDatabaseDecision.NoDatabaseFound);
    }

    // ---------------------------------------------------------------------
    // 4) Base historique (EnsureCreated) compatible
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_CompatibleHistorical_RecommendsAdoption_WithTechnicalMetadata()
    {
        var dbPath = PathFor("historical.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Alice", LastName = "Historique" });
            seed.Customers.Add(new Customer { FirstName = "Bruno", LastName = "Historique" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var result = Diagnose(dbPath);

        result.IsValidSqlite.Should().BeTrue();
        result.HasApplicationTables.Should().BeTrue();
        result.HasMigrationsHistory.Should().BeFalse("une base EnsureCreated n'a pas d'historique");
        result.IsSchemaCompatible.Should().BeTrue();
        result.SchemaDifferences.Should().BeEmpty();
        result.RecommendedDecision.Should().Be(HistoricalDatabaseDecision.AdoptCompatibleHistoricalDatabase);

        result.FileSizeBytes.Should().BeGreaterThan(0);
        result.LastModifiedUtc.Should().NotBeNull();
        result.TableRowCounts.Should().ContainKey("Customers").WhoseValue.Should().Be(2);
        result.MissingMigrations.Should().NotBeEmpty("aucune migration n'est encore inscrite");
        result.AppliedMigrations.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // 5) Base déjà gérée par migrations
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_MigrationsManagedDatabase_RecommendsUseAsCurrent()
    {
        var dbPath = PathFor("managed.db");
        List<string> definedMigrations;
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.Migrate();
            definedMigrations = seed.Database.GetMigrations().ToList();
        }
        SqliteConnection.ClearAllPools();

        var result = Diagnose(dbPath);

        result.HasMigrationsHistory.Should().BeTrue();
        result.IsSchemaCompatible.Should().BeTrue();
        result.RecommendedDecision.Should().Be(HistoricalDatabaseDecision.UseAsCurrentDatabase);
        result.AppliedMigrations.Should().BeEquivalentTo(definedMigrations);
        result.MissingMigrations.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // 6) Base historique incompatible (schéma ancien / partiel)
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_IncompatibleHistorical_RecommendsManualRepair_WithDifferences()
    {
        var dbPath = PathFor("incompatible.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Carla", LastName = "Garder" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE \"Notifications\"";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var result = Diagnose(dbPath);

        result.IsValidSqlite.Should().BeTrue();
        result.HasApplicationTables.Should().BeTrue();
        result.IsSchemaCompatible.Should().BeFalse();
        result.SchemaDifferences.Should().Contain(d => d.Contains("Notifications"));
        result.RecommendedDecision.Should().Be(HistoricalDatabaseDecision.ManualRepairRequired);
    }

    // ---------------------------------------------------------------------
    // Lecture seule : le diagnostic ne mute jamais la base
    // ---------------------------------------------------------------------

    [Fact]
    public void Diagnose_HistoricalDatabase_DoesNotMutateIt()
    {
        var dbPath = PathFor("readonly.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Diane", LastName = "Intacte" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        _ = Diagnose(dbPath);
        SqliteConnection.ClearAllPools();

        // __EFMigrationsHistory toujours absent (aucune adoption), données intactes.
        var after = Diagnose(dbPath);
        after.HasMigrationsHistory.Should().BeFalse("le diagnostic n'inscrit jamais d'historique");
        after.TableRowCounts["Customers"].Should().Be(1);
    }
}
