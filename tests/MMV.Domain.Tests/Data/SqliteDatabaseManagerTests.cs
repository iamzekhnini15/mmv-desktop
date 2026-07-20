using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1A — cycle de vie SQLite : base vierge, base historique (sur copie), adoption des migrations,
/// sauvegarde, restauration, échec contrôlé, journal. Tous les tests utilisent des fichiers temporaires
/// isolés (jamais la base utilisateur réelle).
/// </summary>
public sealed class SqliteDatabaseManagerTests : IDisposable
{
    private readonly string _workDirectory;

    public SqliteDatabaseManagerTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDirectory);
    }

    public void Dispose()
    {
        // Libère les fichiers SQLite (le pooling est désactivé, mais on force par sécurité).
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

    // ---------------------------------------------------------------------
    // Base vierge (installation neuve)
    // ---------------------------------------------------------------------

    [Fact]
    public void PrepareDatabase_FreshInstall_AppliesMigrations_AndRecordsHistory()
    {
        var dbPath = PathFor("fresh.db");
        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        File.Exists(dbPath).Should().BeFalse();

        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }

        result.DetectedState.Should().Be(DatabaseState.Empty);
        result.WasFreshInstall.Should().BeTrue();
        result.BackupPath.Should().BeNull("aucune base préexistante à sauvegarder");

        File.Exists(dbPath).Should().BeTrue("la base est créée au chemin configuré (environnement de test)");
        GetTableNames(dbPath).Should().Contain("__EFMigrationsHistory");

        using (var verify = CreateContext(dbPath))
        {
            verify.Database.GetAppliedMigrations().Should().BeEquivalentTo(verify.Database.GetMigrations());
            verify.Database.GetPendingMigrations().Should().BeEmpty();
        }
    }

    [Fact]
    public void PrepareDatabase_FreshInstall_DoesNotSeedAnyDemoData()
    {
        var dbPath = PathFor("fresh-nodemo.db");
        var manager = new SqliteDatabaseManager();

        using (var context = CreateContext(dbPath))
        {
            manager.PrepareDatabase(context);
        }

        using var verify = CreateContext(dbPath);
        verify.Users.Count().Should().Be(0, "le cycle de vie ne seede aucune donnée");
        verify.Customers.Count().Should().Be(0);
        verify.Products.Count().Should().Be(0);
    }

    // ---------------------------------------------------------------------
    // Preuve : Migrate() et EnsureCreated() produisent le même schéma structurel
    // ---------------------------------------------------------------------

    [Fact]
    public void Migrate_And_EnsureCreated_ProduceEquivalentSchema()
    {
        var migratePath = PathFor("by-migrate.db");
        var ensurePath = PathFor("by-ensure.db");

        using (var migrated = CreateContext(migratePath))
        {
            migrated.Database.Migrate();
        }

        using (var ensured = CreateContext(ensurePath))
        {
            ensured.Database.EnsureCreated();
        }

        var migratedSchema = ReadApplicationSchema(migratePath);
        var ensuredSchema = ReadApplicationSchema(ensurePath);

        migratedSchema.Should().BeEquivalentTo(ensuredSchema,
            "les migrations doivent refléter le modèle courant (aucune migration destructive)");
    }

    // ---------------------------------------------------------------------
    // Base historique (créée par EnsureCreated, sans __EFMigrationsHistory) — testée sur copie
    // ---------------------------------------------------------------------

    [Fact]
    public void PrepareDatabase_HistoricalDatabaseOnCopy_IsAdopted_WithoutDataLoss()
    {
        // 1) Construire une base "historique" via EnsureCreated + données réelles.
        var historicalPath = PathFor("historical.db");
        using (var historical = CreateContext(historicalPath))
        {
            historical.Database.EnsureCreated();
            historical.Customers.Add(new Customer { FirstName = "Alice", LastName = "Historique" });
            historical.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        GetTableNames(historicalPath).Should().NotContain("__EFMigrationsHistory",
            "une base EnsureCreated n'a pas d'historique de migration");

        // 2) Travailler sur une COPIE de la base historique.
        var copyPath = PathFor("historical-copy.db");
        File.Copy(historicalPath, copyPath);

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        DatabasePreparationResult result;
        using (var context = CreateContext(copyPath))
        {
            result = manager.PrepareDatabase(context);
        }

        result.DetectedState.Should().Be(DatabaseState.HistoricalWithoutMigrationsHistory);
        result.WasAdopted.Should().BeTrue();
        result.BaselinedMigrations.Should().NotBeEmpty();
        result.BackupPath.Should().NotBeNull("une sauvegarde est créée avant l'adoption");

        // 3) Historique inscrit, schéma adopté, données intactes.
        GetTableNames(copyPath).Should().Contain("__EFMigrationsHistory");
        using var verify = CreateContext(copyPath);
        verify.Database.GetPendingMigrations().Should().BeEmpty();
        verify.Database.GetAppliedMigrations().Should().BeEquivalentTo(verify.Database.GetMigrations());
        verify.Customers.Should().ContainSingle(c => c.FirstName == "Alice" && c.LastName == "Historique");
    }

    // ---------------------------------------------------------------------
    // Base déjà gérée par migrations (avec __EFMigrationsHistory)
    // ---------------------------------------------------------------------

    [Fact]
    public void PrepareDatabase_AlreadyMigratedDatabase_IsIdempotent_AndBacksUp()
    {
        var dbPath = PathFor("managed.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.Migrate();
            seed.Customers.Add(new Customer { FirstName = "Bob", LastName = "Géré" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var manager = new SqliteDatabaseManager();
        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }

        result.DetectedState.Should().Be(DatabaseState.MigrationsManaged);
        result.WasFreshInstall.Should().BeFalse();
        result.WasAdopted.Should().BeFalse();
        result.AppliedMigrations.Should().BeEmpty("aucune migration en attente");
        result.BackupPath.Should().NotBeNull("la base existante est sauvegardée avant toute mutation");

        using var verify = CreateContext(dbPath);
        verify.Customers.Should().ContainSingle(c => c.FirstName == "Bob");
    }

    // ---------------------------------------------------------------------
    // Sauvegarde avant mutation
    // ---------------------------------------------------------------------

    [Fact]
    public void PrepareDatabase_ExistingDatabase_CreatesVerifiableBackupBeforeMutation()
    {
        var dbPath = PathFor("to-backup.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Carla", LastName = "Sauvegarde" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var manager = new SqliteDatabaseManager();
        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }

        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();

        // La sauvegarde est une base SQLite valide contenant l'état pré-mutation (avant baseline).
        var backupTables = GetTableNames(result.BackupPath!);
        backupTables.Should().Contain("Customers");
        backupTables.Should().NotContain("__EFMigrationsHistory",
            "la sauvegarde reflète l'état historique d'origine, avant adoption");
    }

    // ---------------------------------------------------------------------
    // Restauration après erreur simulée
    // ---------------------------------------------------------------------

    [Fact]
    public void Restore_AfterUnwantedMutation_RecoversBackedUpState()
    {
        var dbPath = PathFor("restore.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Diane", LastName = "Origine" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var manager = new SqliteDatabaseManager();
        var backupPath = manager.Backup(dbPath);

        // Mutation indésirable (simule une opération à annuler).
        using (var mutate = CreateContext(dbPath))
        {
            mutate.Customers.Add(new Customer { FirstName = "Erreur", LastName = "ÀAnnuler" });
            mutate.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var beforeRestore = CreateContext(dbPath))
        {
            beforeRestore.Customers.Count().Should().Be(2);
        }
        SqliteConnection.ClearAllPools();

        // Restauration de la sauvegarde.
        manager.Restore(backupPath, dbPath);
        SqliteConnection.ClearAllPools();

        using var afterRestore = CreateContext(dbPath);
        afterRestore.Customers.Should().ContainSingle(c => c.FirstName == "Diane");
    }

    // ---------------------------------------------------------------------
    // Échec contrôlé si la base est invalide (aucune suppression silencieuse)
    // ---------------------------------------------------------------------

    [Fact]
    public void PrepareDatabase_InvalidDatabaseFile_ThrowsControlledException_AndPreservesFile()
    {
        var dbPath = PathFor("corrupt.db");
        var garbage = new byte[] { 0x4D, 0x4D, 0x56, 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE };
        File.WriteAllBytes(dbPath, garbage);

        var manager = new SqliteDatabaseManager();

        using (var context = CreateContext(dbPath))
        {
            var act = () => manager.PrepareDatabase(context);
            act.Should().Throw<DatabaseMigrationException>();
        }

        // Aucune suppression silencieuse : le fichier d'origine est conservé tel quel.
        File.Exists(dbPath).Should().BeTrue();
        File.ReadAllBytes(dbPath).Should().Equal(garbage);
    }

    // ---------------------------------------------------------------------
    // Journal de migration
    // ---------------------------------------------------------------------

    [Fact]
    public void PrepareDatabase_WritesMigrationJournal()
    {
        var dbPath = PathFor("journalled.db");
        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        using (var context = CreateContext(dbPath))
        {
            manager.PrepareDatabase(context);
        }

        journal.Entries.Should().NotBeEmpty();
        journal.Entries.Should().Contain(e => e.Contains("DETECT state="));
        journal.Entries.Should().Contain(e => e.Contains("FRESH"));
    }

    [Fact]
    public void MigrationJournal_WithFile_AppendsEntriesToDisk()
    {
        var journalPath = PathFor("journal.log");
        var journal = new MigrationJournal(journalPath);

        journal.Write("ligne-de-test");

        File.Exists(journalPath).Should().BeTrue();
        File.ReadAllText(journalPath).Should().Contain("ligne-de-test");
    }

    // ---------------------------------------------------------------------
    // P2A-1A-R2 — Portail de compatibilité de schéma avant baseline
    // ---------------------------------------------------------------------

    [Fact]
    public void SchemaVerifier_ValidEnsureCreatedDatabase_IsCompatible()
    {
        var dbPath = PathFor("valid-schema.db");
        using var context = CreateContext(dbPath);
        context.Database.EnsureCreated();

        var result = new SqliteSchemaVerifier().Verify(context);

        result.IsCompatible.Should().BeTrue(
            "une base EnsureCreated reflète le modèle courant — aucun faux positif. Divergences : {0}",
            string.Join(" ; ", result.Differences));
    }

    [Fact]
    public void PrepareDatabase_Historical_MissingTable_RefusesAdoption()
        => AssertHistoricalAdoptionRefused(
            conn => Exec(conn, "DROP TABLE \"Notifications\""),
            "table manquante");

    [Fact]
    public void PrepareDatabase_Historical_MissingColumn_RefusesAdoption()
        => AssertHistoricalAdoptionRefused(
            conn => Exec(conn, "ALTER TABLE \"Customers\" DROP COLUMN \"Notes\""),
            "colonne manquante");

    [Fact]
    public void PrepareDatabase_Historical_IncompatibleColumnType_RefusesAdoption()
        => AssertHistoricalAdoptionRefused(
            conn =>
            {
                Exec(conn, "ALTER TABLE \"Customers\" DROP COLUMN \"Notes\"");
                Exec(conn, "ALTER TABLE \"Customers\" ADD COLUMN \"Notes\" INTEGER");
            },
            "type incompatible");

    [Fact]
    public void PrepareDatabase_Historical_MissingForeignKey_RefusesAdoption()
        => AssertHistoricalAdoptionRefused(
            conn => RemoveForeignKeys(conn, "Prescriptions"),
            "clé étrangère manquante");

    [Fact]
    public void PrepareDatabase_Historical_MissingUniqueIndex_RefusesAdoption()
        => AssertHistoricalAdoptionRefused(
            conn =>
            {
                var indexName = ScalarOrNull(conn,
                    "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='ProductCategories' AND name NOT LIKE 'sqlite_%'");
                indexName.Should().NotBeNull("ProductCategories possède un index unique sur Name");
                Exec(conn, $"DROP INDEX \"{indexName}\"");
            },
            "index unique manquant");

    [Fact]
    public void PrepareDatabase_PartiallyOldSchema_RefusesAdoption_WithoutWritingHistory()
    {
        // Base "ancienne" minimale construite à la main : une table applicative partielle, sans historique.
        var dbPath = PathFor("partially-old.db");
        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open();
            Exec(connection, "CREATE TABLE \"Users\" (\"UserId\" INTEGER PRIMARY KEY AUTOINCREMENT, \"Username\" TEXT)");
        }
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        using (var context = CreateContext(dbPath))
        {
            var act = () => manager.PrepareDatabase(context);
            act.Should().Throw<DatabaseMigrationException>();
        }
        SqliteConnection.ClearAllPools();

        GetTableNames(dbPath).Should().NotContain("__EFMigrationsHistory",
            "aucune migration ne doit être inscrite pour une base incompatible");
        journal.Entries.Should().Contain(e => e.Contains("ADOPT REFUSED"));
    }

    // ---------------------------------------------------------------------
    // P3-8 — Adoption partielle du schéma de résolution des notifications
    // ---------------------------------------------------------------------
    //
    // AddNotificationResolution ajoute, dans UNE seule migration, la colonne Notifications.ResolvedAt ET l'index
    // unique filtré idx_notifications_active_low_stock_unique. Comme pour R-19 / DocumentSequences / IsArchived /
    // WorkshopSheets, une base historique peut avoir été créée AVANT cette migration : EnsureCreated() construit
    // toujours le modèle COURANT, donc pour simuler une base antérieure on construit le schéma courant puis on
    // retire chirurgicalement la colonne et/ou l'index P3-8 avant adoption.

    private static List<string> ColumnNames(string databasePath, string table)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static bool IndexExists(string databasePath, string indexName)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly");
        connection.Open();
        return ScalarOrNull(connection, $"SELECT name FROM sqlite_master WHERE type='index' AND name='{indexName}'") != null;
    }

    [Fact]
    public void PrepareDatabase_Historical_MissingNotificationResolutionSchema_ExecutesMigration_WithoutDataLoss()
    {
        // État "absent / absent / absent" (§3.2, ligne 1) : aucune trace de P3-8 -> la migration doit s'EXÉCUTER
        // réellement (pas être baselinée), comme R-19/DocumentSequences/IsArchived/WorkshopSheets avant elle.
        var dbPath = PathFor("historical-p38-absent.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Ivy", LastName = "Historique" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open();
            Exec(connection, "DROP INDEX \"idx_notifications_active_low_stock_unique\"");
            Exec(connection, "ALTER TABLE \"Notifications\" DROP COLUMN \"ResolvedAt\"");
        }
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }

        result.WasAdopted.Should().BeTrue();
        journal.Entries.Should().Contain(e => e.Contains("AddNotificationResolution will be executed"));

        using var verify = CreateContext(dbPath);
        verify.Database.GetPendingMigrations().Should().BeEmpty();
        verify.Database.GetAppliedMigrations().Should().Contain(m => m.EndsWith("_AddNotificationResolution", StringComparison.Ordinal));
        verify.Customers.Should().ContainSingle(c => c.FirstName == "Ivy", "l'exécution réelle de la migration ne doit perdre aucune donnée");

        ColumnNames(dbPath, "Notifications").Should().Contain("ResolvedAt");
        IndexExists(dbPath, "idx_notifications_active_low_stock_unique").Should().BeTrue();
    }

    [Fact]
    public void PrepareDatabase_Historical_NotificationSchemaAlreadyComplete_BaselinesWithoutReplayingAddColumn()
    {
        // État "absent (historique) / présent / présent" (§3.2, ligne 3) : le schéma P3-8 est déjà physiquement
        // complet (cas réel : EnsureCreated() construit toujours le modèle courant) -> baseline normal, aucune
        // exécution forcée de AddNotificationResolution.
        var dbPath = PathFor("historical-p38-complete.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
        }
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }

        result.WasAdopted.Should().BeTrue();
        result.BaselinedMigrations.Should().Contain(m => m.EndsWith("_AddNotificationResolution", StringComparison.Ordinal));
        result.AppliedMigrations.Should().NotContain(m => m.EndsWith("_AddNotificationResolution", StringComparison.Ordinal),
            "le schéma étant déjà physiquement complet, la migration doit être BASELINÉE, jamais rejouée");
        journal.Entries.Should().NotContain(e => e.Contains("AddNotificationResolution will be executed"));
    }

    [Fact]
    public void PrepareDatabase_Historical_ResolvedAtPresentButIndexMissing_RefusesAdoption()
        // État "absent / présent / absent" (§3.2, ligne 4) : schéma partiel -> refus explicite, jamais un baseline
        // qui affirmerait à tort une protection multi-poste inexistante.
        => AssertHistoricalAdoptionRefused(
            conn => Exec(conn, "DROP INDEX \"idx_notifications_active_low_stock_unique\""),
            "partiel");

    [Fact]
    public void PrepareDatabase_Historical_ResolvedAtPresentButIndexFilterWrong_RefusesAdoption()
        // Variante de la ligne 4/8 : un index du même nom existe, UNIQUE, sur les bonnes colonnes, mais SANS la
        // restriction de type — exactement le piège signalé par l'audit (bloquerait les transitions de commande).
        // Il doit être détecté comme mal défini, pas seulement comme "absent".
        => AssertHistoricalAdoptionRefused(
            conn =>
            {
                Exec(conn, "DROP INDEX \"idx_notifications_active_low_stock_unique\"");
                Exec(conn,
                    "CREATE UNIQUE INDEX \"idx_notifications_active_low_stock_unique\" ON \"Notifications\" " +
                    "(\"Type\", \"EntityType\", \"EntityId\") WHERE \"EntityId\" IS NOT NULL AND \"ResolvedAt\" IS NULL");
            },
            "partiel");

    [Fact]
    public void PrepareDatabase_Historical_IndexHomonymPresentButResolvedAtMissing_RefusesAdoption()
        // État "absent / absent / index homonyme mal défini" (§3.2, ligne 5) : un index de même nom existe sans
        // rapport avec la colonne (jamais produit par une exécution normale de la migration, mais une base altérée
        // à la main peut le présenter) -> refus explicite, jamais confondu avec "absence normale".
        => AssertHistoricalAdoptionRefused(
            conn =>
            {
                Exec(conn, "DROP INDEX \"idx_notifications_active_low_stock_unique\"");
                Exec(conn, "ALTER TABLE \"Notifications\" DROP COLUMN \"ResolvedAt\"");
                Exec(conn, "CREATE INDEX \"idx_notifications_active_low_stock_unique\" ON \"Notifications\" (\"Type\")");
            },
            "incohérent");

    [Fact]
    public void PrepareDatabase_ManagedDatabase_TamperedNotificationSchema_ThrowsInconsistentHistory()
    {
        // États "présent (historique) / absent ou mal défini" (§3.2, lignes 6-8) : une migration transactionnelle
        // normale ne peut pas les produire (Migrate() est tout-ou-rien), mais une base altérée à la main APRÈS
        // une migration réussie le peut. __EFMigrationsHistory affirme AddNotificationResolution appliquée alors
        // que le schéma physique ne la reflète plus -> jamais accepté silencieusement, même hors adoption.
        var dbPath = PathFor("managed-p38-tampered.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open();
            Exec(connection, "DROP INDEX \"idx_notifications_active_low_stock_unique\"");
            Exec(connection, "ALTER TABLE \"Notifications\" DROP COLUMN \"ResolvedAt\"");
        }
        SqliteConnection.ClearAllPools();

        var manager = new SqliteDatabaseManager();
        using var context = CreateContext(dbPath);
        var act = () => manager.PrepareDatabase(context);

        act.Should().Throw<DatabaseMigrationException>()
            .Which.Message.Should().Contain("AddNotificationResolution");
    }

    [Fact]
    public void Baseline_WritesAndReadsBackMigrationHistory()
    {
        // Base historique valide (EnsureCreated + données).
        var dbPath = PathFor("baseline-history.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Henri", LastName = "Historique" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var manager = new SqliteDatabaseManager();
        List<string> expectedMigrations;
        using (var context = CreateContext(dbPath))
        {
            expectedMigrations = context.Database.GetMigrations().ToList();
            manager.PrepareDatabase(context);
        }
        SqliteConnection.ClearAllPools();

        // Relecture directe de __EFMigrationsHistory (écriture encapsulée -> relecture).
        ReadMigrationHistoryIds(dbPath).Should().BeEquivalentTo(expectedMigrations);

        // Relecture via EF.
        using var verify = CreateContext(dbPath);
        verify.Database.GetAppliedMigrations().Should().BeEquivalentTo(expectedMigrations);
        verify.Customers.Should().ContainSingle(c => c.FirstName == "Henri");
    }

    /// <summary>
    /// Construit une base historique valide (EnsureCreated + 1 client), applique une altération de
    /// schéma rendant la base incompatible, puis vérifie que l'adoption est REFUSÉE : sauvegarde créée,
    /// exception contrôlée, __EFMigrationsHistory NON inscrite, base préservée, journal motivé.
    /// </summary>
    private void AssertHistoricalAdoptionRefused(Action<SqliteConnection> corrupt, string expectedReasonFragment)
    {
        var dbPath = PathFor("incompatible.db");
        using (var seed = CreateContext(dbPath))
        {
            seed.Database.EnsureCreated();
            seed.Customers.Add(new Customer { FirstName = "Garder", LastName = "Intact" });
            seed.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            connection.Open();
            corrupt(connection);
        }
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        using (var context = CreateContext(dbPath))
        {
            var act = () => manager.PrepareDatabase(context);
            act.Should().Throw<DatabaseMigrationException>()
                .Which.Message.Should().Contain(expectedReasonFragment);
        }
        SqliteConnection.ClearAllPools();

        // Sauvegarde créée AVANT le refus.
        var backupsDirectory = Path.Combine(_workDirectory, "backups");
        Directory.Exists(backupsDirectory).Should().BeTrue();
        Directory.GetFiles(backupsDirectory).Should().NotBeEmpty();

        // Aucune inscription d'historique.
        GetTableNames(dbPath).Should().NotContain("__EFMigrationsHistory");

        // Base préservée (le fichier existe toujours).
        File.Exists(dbPath).Should().BeTrue();

        // Journal motivé.
        journal.Entries.Should().Contain(e => e.Contains("ADOPT REFUSED"));
    }

    // ---------------------------------------------------------------------
    // Helpers de lecture de schéma (ADO.NET direct, hors EF)
    // ---------------------------------------------------------------------

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

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? ScalarOrNull(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// Reconstruit une table en supprimant ses contraintes de clé étrangère (dérivées du CREATE réel,
    /// pour rester robuste). Simule une base historique sans FK essentielles.
    /// </summary>
    private static void RemoveForeignKeys(SqliteConnection connection, string table)
    {
        var createSql = ScalarOrNull(connection,
            $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table}'");
        createSql.Should().NotBeNull();

        var keptLines = createSql!
            .Split('\n')
            .Where(line => line.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) < 0);
        var rebuilt = string.Join("\n", keptLines);
        // Supprime la virgule orpheline laissée avant la parenthèse fermante.
        rebuilt = Regex.Replace(rebuilt, ",(\\s*)\\)", "$1)");

        Exec(connection, "PRAGMA foreign_keys=OFF");
        Exec(connection, $"ALTER TABLE \"{table}\" RENAME TO \"{table}_old\"");
        Exec(connection, rebuilt);
        Exec(connection, $"INSERT INTO \"{table}\" SELECT * FROM \"{table}_old\"");
        Exec(connection, $"DROP TABLE \"{table}_old\"");
    }

    private static List<string> ReadMigrationHistoryIds(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM \"__EFMigrationsHistory\"";
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    /// <summary>
    /// Lit le schéma applicatif (tables → colonnes "nom|type|notnull|pk"), en excluant les tables
    /// internes (__*, sqlite_*).
    /// </summary>
    private static SortedDictionary<string, SortedSet<string>> ReadApplicationSchema(string databasePath)
    {
        var schema = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly");
        connection.Open();

        var tables = new List<string>();
        using (var tablesCommand = connection.CreateCommand())
        {
            tablesCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            using var reader = tablesCommand.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (!name.StartsWith("__", StringComparison.Ordinal)
                    && !name.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
                {
                    tables.Add(name);
                }
            }
        }

        foreach (var table in tables)
        {
            var columns = new SortedSet<string>(StringComparer.Ordinal);
            using var columnsCommand = connection.CreateCommand();
            columnsCommand.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = columnsCommand.ExecuteReader();
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                var columnType = reader.GetString(2);
                var notNull = reader.GetInt32(3);
                var primaryKey = reader.GetInt32(5);
                columns.Add($"{columnName}|{columnType}|{notNull}|{primaryKey}");
            }

            schema[table] = columns;
        }

        return schema;
    }
}
