using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1B — reprise contrôlée de l'ancien fichier <c>mmv-optic.db</c> vers le chemin courant.
/// Couvre les 12 scénarios obligatoires : ancien absent, présent/invalide, valide+compatible,
/// valide+incompatible, nouveau déjà présent, conflit, sauvegarde, rapport avant/après, absence de
/// donnée personnelle, conservation des comptes, refus contrôlé. Fichiers temporaires uniquement ;
/// l'ancien fichier n'est jamais supprimé ni écrasé.
/// </summary>
public sealed class LegacyDatabaseRecoveryServiceTests : IDisposable
{
    private readonly string _workDirectory;

    public LegacyDatabaseRecoveryServiceTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-legacy-" + Guid.NewGuid().ToString("N"));
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

    private OpticDbContext Factory(string path) => CreateContext(path);

    /// <summary>Construit une base historique valide (EnsureCreated) peuplée de <paramref name="customers"/> clients.</summary>
    private void SeedHistorical(string path, params string[] customers)
    {
        using (var seed = CreateContext(path))
        {
            seed.Database.EnsureCreated();
            foreach (var name in customers)
            {
                seed.Customers.Add(new Customer { FirstName = name, LastName = "Legacy" });
            }

            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
    }

    private static List<string> GetTableNames(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly");
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

    // ---------------------------------------------------------------------
    // 1 & 2) Aucun ancien fichier / ancien absent
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_NoLegacyFile_DoesNothing_AndDoesNotCreateCurrent()
    {
        var legacyPath = PathFor("mmv-optic.db"); // absent
        var currentPath = PathFor("mmv.db");      // absent
        var journal = new MigrationJournal();

        var result = new LegacyDatabaseRecoveryService(journal).Recover(legacyPath, currentPath, Factory);

        result.Decision.Should().Be(HistoricalDatabaseDecision.NoDatabaseFound);
        result.CopyPerformed.Should().BeFalse();
        File.Exists(currentPath).Should().BeFalse("aucune copie ne doit avoir lieu sans ancien fichier");
        journal.Entries.Should().Contain(e => e.Contains("LEGACY none"));
    }

    // ---------------------------------------------------------------------
    // 3) Ancien présent mais invalide
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_InvalidLegacyFile_RefusesAndPreservesIt()
    {
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        var garbage = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
        File.WriteAllBytes(legacyPath, garbage);
        var journal = new MigrationJournal();

        var result = new LegacyDatabaseRecoveryService(journal).Recover(legacyPath, currentPath, Factory);

        result.Decision.Should().Be(HistoricalDatabaseDecision.RejectInvalidDatabase);
        result.CopyPerformed.Should().BeFalse();
        File.Exists(currentPath).Should().BeFalse();
        File.ReadAllBytes(legacyPath).Should().Equal(garbage, "l'ancien fichier ne doit être ni modifié ni supprimé");
        journal.Entries.Should().Contain(e => e.Contains("LEGACY rejected"));
    }

    // ---------------------------------------------------------------------
    // 4 & 8 & 11) Ancien valide+compatible, chemin courant absent → copie contrôlée
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_ValidCompatibleLegacy_CurrentAbsent_CopiesAdoptsAndBacksUp()
    {
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        SeedHistorical(legacyPath, "Alice", "Bruno");
        var journal = new MigrationJournal();

        var result = new LegacyDatabaseRecoveryService(journal).Recover(legacyPath, currentPath, Factory);

        result.Decision.Should().Be(HistoricalDatabaseDecision.CopyLegacyDatabaseToCurrentPath);
        result.CopyPerformed.Should().BeTrue();

        // (8) Sauvegarde créée avant la copie.
        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();
        result.Report.BackupCreated.Should().BeTrue();

        // L'ancien fichier est conservé (ni supprimé, ni vidé).
        File.Exists(legacyPath).Should().BeTrue();

        // Le chemin courant contient désormais une base adoptée (migrations inscrites), données préservées.
        File.Exists(currentPath).Should().BeTrue();
        GetTableNames(currentPath).Should().Contain("__EFMigrationsHistory");
        using (var verify = CreateContext(currentPath))
        {
            verify.Database.GetPendingMigrations().Should().BeEmpty();
            verify.Customers.Should().HaveCount(2);
        }

        // (11) Comptes de lignes conservés avant/après.
        result.Report.RowCountsBefore["Customers"].Should().Be(2);
        result.Report.RowCountsAfter["Customers"].Should().Be(2);
        result.Report.RowCountsPreserved.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // 5 & 12) Ancien valide mais incompatible → refus contrôlé, sans copie ni historique
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_IncompatibleLegacy_RefusesManualRepair_WithoutCopyingOrWritingHistory()
    {
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        SeedHistorical(legacyPath, "Carla");

        // Rendre le schéma incompatible (table applicative manquante).
        using (var connection = new SqliteConnection($"Data Source={legacyPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE \"Notifications\"";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var result = new LegacyDatabaseRecoveryService(journal).Recover(legacyPath, currentPath, Factory);

        result.Decision.Should().Be(HistoricalDatabaseDecision.ManualRepairRequired);
        result.CopyPerformed.Should().BeFalse();
        File.Exists(currentPath).Should().BeFalse("aucune copie d'un schéma incompatible");
        File.Exists(legacyPath).Should().BeTrue("l'ancien fichier est conservé pour le support");
        result.Report.DivergentColumns.Should().NotBeEmpty();
        journal.Entries.Should().Contain(e => e.Contains("LEGACY refused"));
    }

    // ---------------------------------------------------------------------
    // 6 & 7) Conflit : nouveau mmv.db déjà présent ET ancien présent
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_BothFilesPresent_DetectsConflict_KeepsBothUntouched()
    {
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        SeedHistorical(legacyPath, "Ancien");

        // Une base existe déjà au chemin courant (gérée par migrations + une donnée distincte).
        using (var current = CreateContext(currentPath))
        {
            current.Database.Migrate();
            current.Customers.Add(new Customer { FirstName = "Courant", LastName = "Déjà" });
            current.SaveChanges();
        }
        SqliteConnection.ClearAllPools();
        var legacyBytesBefore = File.ReadAllBytes(legacyPath);

        var journal = new MigrationJournal();
        var result = new LegacyDatabaseRecoveryService(journal).Recover(legacyPath, currentPath, Factory);

        result.ConflictDetected.Should().BeTrue();
        result.CopyPerformed.Should().BeFalse();
        result.Decision.Should().Be(HistoricalDatabaseDecision.UseAsCurrentDatabase);

        // Ni l'ancien ni le courant ne sont écrasés.
        File.ReadAllBytes(legacyPath).Should().Equal(legacyBytesBefore, "l'ancien fichier reste intact en cas de conflit");
        using (var verify = CreateContext(currentPath))
        {
            verify.Customers.Should().ContainSingle(c => c.FirstName == "Courant");
        }

        journal.Entries.Should().Contain(e => e.Contains("LEGACY conflict"));
    }

    // ---------------------------------------------------------------------
    // 9) Rapport avant/après généré
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_ProducesReadableBeforeAfterReport()
    {
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        SeedHistorical(legacyPath, "Alice", "Bruno", "Carla");

        var result = new LegacyDatabaseRecoveryService().Recover(legacyPath, currentPath, Factory);

        var rendered = result.Report.Render();
        rendered.Should().Contain("Rapport de reprise de base historique");
        rendered.Should().Contain("Comptes de lignes (avant / après)");
        rendered.Should().Contain("Customers: 3 -> 3");
        rendered.Should().Contain(HistoricalDatabaseDecision.CopyLegacyDatabaseToCurrentPath.ToString());
    }

    // ---------------------------------------------------------------------
    // 10) Aucun contenu personnel dans le rapport
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_Report_ContainsNoPersonalData()
    {
        const string personalName = "JeanPrivéSecret";
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        SeedHistorical(legacyPath, personalName);

        var result = new LegacyDatabaseRecoveryService().Recover(legacyPath, currentPath, Factory);

        var rendered = result.Report.Render();
        rendered.Should().NotContain(personalName,
            "le rapport ne doit exposer que des métadonnées techniques (noms de tables, comptes), jamais de données personnelles");
        // Le compte agrégé est bien présent (métadonnée), sans le contenu personnel.
        rendered.Should().Contain("Customers: 1 -> 1");
    }

    // ---------------------------------------------------------------------
    // Ancien valide mais vide → rien à reprendre
    // ---------------------------------------------------------------------

    [Fact]
    public void Recover_EmptyValidLegacy_DoesNothing()
    {
        var legacyPath = PathFor("mmv-optic.db");
        var currentPath = PathFor("mmv.db");
        using (var connection = new SqliteConnection($"Data Source={legacyPath};Pooling=False"))
        {
            connection.Open(); // base SQLite valide mais vide
        }
        SqliteConnection.ClearAllPools();

        var result = new LegacyDatabaseRecoveryService().Recover(legacyPath, currentPath, Factory);

        result.Decision.Should().Be(HistoricalDatabaseDecision.NoDatabaseFound);
        result.CopyPerformed.Should().BeFalse();
        File.Exists(currentPath).Should().BeFalse();
    }
}
