using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1R19-R2 — bases historiques sans <c>__EFMigrationsHistory</c> contenant les anciens DEFAULT
/// DateTime figés (R-19). Le portail de compatibilité <see cref="SqliteSchemaVerifier"/> ne compare pas
/// les valeurs DEFAULT : une telle base est structurellement « compatible » mais doit être <b>réparée</b>
/// (et non baselinée à tort). Stratégie retenue (Option A) : baseline limité aux migrations réellement
/// reflétées par le schéma physique, puis exécution réelle de <c>FixDateTimeDefaultValues</c>, enfin
/// vérification physique post-adoption (aucun DEFAULT hérité ne subsiste).
///
/// Tous les tests utilisent des fichiers temporaires isolés (jamais la base utilisateur réelle).
/// </summary>
public sealed class SqliteHistoricalDateTimeDefaultsTests : IDisposable
{
    /// <summary>Dernière migration AVANT le correctif R-19 : son schéma porte les anciens DEFAULT figés.</summary>
    private const string MigrationBeforeFix = "20260212220902_RestoreSaleOrderSeparation";

    /// <summary>Suffixe de la migration technique de correction R-19.</summary>
    private const string FixMigrationSuffix = "_FixDateTimeDefaultValues";

    private readonly string _workDirectory;

    public SqliteHistoricalDateTimeDefaultsTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-r19r2-" + Guid.NewGuid().ToString("N"));
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
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var options = new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OpticDbContext(options);
    }

    // =====================================================================
    // (1)(2)(3) Le vérificateur physique distingue base saine et base pré-R19
    // =====================================================================

    [Fact]
    public void Verifier_FreshMigratedDatabase_ReportsNoLegacyDefaults()
    {
        var dbPath = PathFor("fresh.db");
        using var context = CreateContext(dbPath);
        context.Database.Migrate();

        new SqliteDateTimeDefaultVerifier().GetColumnsWithLegacyDefaults(context)
            .Should().BeEmpty("une base créée via migrations après R-19 n'a aucun DEFAULT DateTime figé");
    }

    [Fact]
    public void Verifier_CurrentEnsureCreatedDatabase_ReportsNoLegacyDefaults()
    {
        var dbPath = PathFor("ensure-current.db");
        using var context = CreateContext(dbPath);
        context.Database.EnsureCreated();

        new SqliteDateTimeDefaultVerifier().HasLegacyDateTimeDefaults(context)
            .Should().BeFalse("EnsureCreated reflète le modèle courant (post-R19) sans DEFAULT figé");
    }

    [Fact]
    public void Verifier_PreR19HistoricalDatabase_DetectsLegacyDefaultsOnAllSevenColumns()
    {
        var dbPath = PathFor("pre-r19.db");
        using var context = CreateContext(dbPath);
        context.GetService<IMigrator>().Migrate(MigrationBeforeFix);

        var detected = new SqliteDateTimeDefaultVerifier().GetColumnsWithLegacyDefaults(context);

        detected.Should().BeEquivalentTo(new[]
        {
            "Users.CreatedAt", "Customers.CreatedAt", "Customers.UpdatedAt",
            "Orders.OrderDate", "Sales.SaleDate", "Prescriptions.CreatedAt", "StockMovements.CreatedAt"
        }, "le schéma antérieur à R-19 porte les 7 anciens DEFAULT DateTime figés");
    }

    // =====================================================================
    // (4)(5) Base historique pré-R19 sans history : RÉPARÉE (pas de baseline mensonger)
    // =====================================================================

    [Fact]
    public void Adopt_PreR19HistoricalWithLegacyDefaults_RepairsByExecutingFixMigration()
    {
        var dbPath = PathFor("historical-pre-r19.db");
        BuildPreR19HistoricalDatabase(dbPath, "Vieux", "Defaut");

        // Pré-conditions : anciens DEFAULT présents, aucune table d'historique.
        GetTableNames(dbPath).Should().NotContain("__EFMigrationsHistory");
        GetColumnDefault(dbPath, "Users", "CreatedAt").Should().NotBeNull("base antérieure à R-19");

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }
        SqliteConnection.ClearAllPools();

        // Adoptée, sauvegardée avant mutation.
        result.DetectedState.Should().Be(DatabaseState.HistoricalWithoutMigrationsHistory);
        result.WasAdopted.Should().BeTrue();
        result.BackupPath.Should().NotBeNull("une sauvegarde est créée avant l'adoption");
        File.Exists(result.BackupPath!).Should().BeTrue();

        // Pas de baseline mensonger : FixDateTimeDefaultValues n'est PAS baselinée mais réellement exécutée.
        result.BaselinedMigrations.Should().NotContain(m => m.EndsWith(FixMigrationSuffix, StringComparison.Ordinal));
        result.AppliedMigrations.Should().Contain(m => m.EndsWith(FixMigrationSuffix, StringComparison.Ordinal));

        // __EFMigrationsHistory cohérent avec le schéma physique : tout est appliqué, rien en attente.
        GetTableNames(dbPath).Should().Contain("__EFMigrationsHistory");
        using (var verify = CreateContext(dbPath))
        {
            verify.Database.GetPendingMigrations().Should().BeEmpty();
            verify.Database.GetAppliedMigrations().Should().BeEquivalentTo(verify.Database.GetMigrations());
            verify.Database.HasPendingModelChanges().Should().BeFalse();

            // Données conservées (reconstruction non destructive).
            verify.Customers.Should().ContainSingle(c => c.FirstName == "Vieux" && c.LastName == "Defaut");
        }

        // Anciens DEFAULT physiquement supprimés sur les 7 colonnes.
        foreach (var (table, column) in SqliteDateTimeDefaultVerifier.CorrectedDateTimeColumns)
        {
            GetColumnDefault(dbPath, table, column)
                .Should().BeNull($"{table}.{column} ne doit plus porter de DEFAULT figé après réparation");
        }

        // Journal motivé.
        journal.Entries.Should().Contain(e => e.Contains("legacy DateTime DEFAULT detected"));
        journal.Entries.Should().Contain(e => e.Contains("FixDateTimeDefaultValues will be executed"));
    }

    [Fact]
    public void Adopt_CurrentEnsureCreatedHistorical_NoLegacyDefaults_BaselinesAllMigrations()
    {
        var dbPath = PathFor("historical-current.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated(); // modèle courant (post-R19) : aucun DEFAULT figé
            seed.Customers.Add(new Customer { FirstName = "Actuel", LastName = "Sain" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        // Pré-condition : aucune colonne corrigée ne porte de DEFAULT.
        foreach (var (table, column) in SqliteDateTimeDefaultVerifier.CorrectedDateTimeColumns)
        {
            GetColumnDefault(dbPath, table, column).Should().BeNull();
        }

        var manager = new SqliteDatabaseManager();
        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }
        SqliteConnection.ClearAllPools();

        result.WasAdopted.Should().BeTrue();
        // Aucun ancien DEFAULT : toutes les migrations sont baselinées (FixDateTimeDefaultValues incluse),
        // aucune n'a besoin d'être exécutée (le schéma reflète déjà le modèle courant).
        result.BaselinedMigrations.Should().Contain(m => m.EndsWith(FixMigrationSuffix, StringComparison.Ordinal));
        result.AppliedMigrations.Should().BeEmpty();

        using var verify = CreateContext(dbPath);
        verify.Database.GetPendingMigrations().Should().BeEmpty();
        verify.Database.GetAppliedMigrations().Should().BeEquivalentTo(verify.Database.GetMigrations());
        verify.Customers.Should().ContainSingle(c => c.FirstName == "Actuel");
    }

    // =====================================================================
    // (6) Reprise de l'ancien mmv-optic.db pré-R19 : copie + réparation au chemin courant
    // =====================================================================

    [Fact]
    public void Recover_LegacyMmvOpticDbWithLegacyDefaults_RepairsCurrentDatabase()
    {
        var legacyPath = PathFor("mmv-optic.db");
        BuildPreR19HistoricalDatabase(legacyPath, "Legacy", "Defaut");

        var currentPath = PathFor("mmv.db");
        File.Exists(currentPath).Should().BeFalse("le chemin courant est absent : reprise autorisée");

        var journal = new MigrationJournal();
        var service = new LegacyDatabaseRecoveryService(journal);

        var result = service.Recover(legacyPath, currentPath, CreateContext);
        SqliteConnection.ClearAllPools();

        result.Decision.Should().Be(HistoricalDatabaseDecision.CopyLegacyDatabaseToCurrentPath);
        result.CopyPerformed.Should().BeTrue();
        File.Exists(currentPath).Should().BeTrue();

        // Base courante reprise ET réparée : plus aucun ancien DEFAULT, historique cohérent, données conservées.
        foreach (var (table, column) in SqliteDateTimeDefaultVerifier.CorrectedDateTimeColumns)
        {
            GetColumnDefault(currentPath, table, column)
                .Should().BeNull($"{table}.{column} réparé après reprise");
        }

        using (var verify = CreateContext(currentPath))
        {
            verify.Database.HasPendingModelChanges().Should().BeFalse();
            verify.Database.GetPendingMigrations().Should().BeEmpty();
            verify.Customers.Should().ContainSingle(c => c.FirstName == "Legacy");
        }

        // L'ancien fichier est conservé intact (jamais supprimé).
        File.Exists(legacyPath).Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Construit une base SQLite « historique » antérieure à P2A-1R19 : migrée jusqu'à la dernière
    /// migration AVANT le correctif (le schéma porte alors les anciens DEFAULT DateTime figés), peuplée
    /// d'un client, puis privée de <c>__EFMigrationsHistory</c> pour simuler une base <c>EnsureCreated</c>.
    /// </summary>
    private void BuildPreR19HistoricalDatabase(string dbPath, string firstName, string lastName)
    {
        using (var context = CreateContext(dbPath))
        {
            context.GetService<IMigrator>().Migrate(MigrationBeforeFix);
            context.Customers.Add(new Customer { FirstName = firstName, LastName = lastName });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        ExecNonQuery(dbPath, "DROP TABLE \"__EFMigrationsHistory\"");
        SqliteConnection.ClearAllPools();
    }

    private static void ExecNonQuery(string dbPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> GetTableNames(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False;Mode=ReadOnly");
        connection.Open();
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

    /// <summary>Renvoie le <c>dflt_value</c> d'une colonne (PRAGMA table_info), ou null si aucun défaut.</summary>
    private static string? GetColumnDefault(string dbPath, string table, string column)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False;Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return reader.IsDBNull(4) ? null : reader.GetString(4);
            }
        }

        return null;
    }
}
