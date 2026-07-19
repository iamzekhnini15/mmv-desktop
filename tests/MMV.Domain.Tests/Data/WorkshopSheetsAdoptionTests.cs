using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Exceptions;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P3-6B — Adoption des bases historiques face aux <b>quatre</b> états de présence des tables de fiche atelier.
///
/// Les deux tables sont créées <b>ensemble</b> par <c>AddWorkshopSheets</c> : n'en trouver qu'une seule est un
/// schéma partiel, qu'aucune adoption automatique ne peut réparer honnêtement. Ces tests prouvent que les deux
/// états complets sont traités normalement, et que les deux états partiels échouent <b>explicitement</b>, sans
/// inscrire la moindre entrée dans <c>__EFMigrationsHistory</c>.
/// </summary>
public sealed class WorkshopSheetsAdoptionTests : IDisposable
{
    /// <summary>Dernière migration AVANT P3-6B : son schéma ne contient aucune table de fiche atelier.</summary>
    private const string MigrationBeforeWorkshopSheets = "20260717183027_AddProductNormalizedReferenceAndProtectHistory";

    private const string WorkshopSheets = "WorkshopSheets";
    private const string WorkshopSheetItems = "WorkshopSheetItems";
    private const string MigrationsHistory = "__EFMigrationsHistory";

    private readonly string _workDirectory;

    public WorkshopSheetsAdoptionTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p36b-adopt-" + Guid.NewGuid().ToString("N"));
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
        var options = new DbContextOptionsBuilder<OpticDbContext>().UseSqlite(connectionString).Options;
        return new OpticDbContext(options);
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

    private static void ExecNonQuery(string dbPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Base historique antérieure à P3-6B : aucune table de fiche, aucun historique de migration.</summary>
    private void BuildPreWorkshopSheetsDatabase(string dbPath)
    {
        using (var context = CreateContext(dbPath))
        {
            context.GetService<IMigrator>().Migrate(MigrationBeforeWorkshopSheets);
        }
        SqliteConnection.ClearAllPools();

        ExecNonQuery(dbPath, $"DROP TABLE \"{MigrationsHistory}\"");
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Base déjà porteuse des deux tables de fiche, puis privée de son historique de migration.</summary>
    private void BuildAdoptedWorkshopSheetsDatabase(string dbPath)
    {
        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        ExecNonQuery(dbPath, $"DROP TABLE \"{MigrationsHistory}\"");
        SqliteConnection.ClearAllPools();
    }

    private static DatabasePreparationResult Prepare(string dbPath, MigrationJournal journal)
    {
        using var context = CreateContext(dbPath);
        var result = new SqliteDatabaseManager(journal).PrepareDatabase(context);
        SqliteConnection.ClearAllPools();
        return result;
    }

    // ===========================================================================================================
    // États complets — les deux issues normales
    // ===========================================================================================================

    [Fact]
    public void DeuxTablesAbsentes_ExecuteLaMigration()
    {
        var dbPath = PathFor("both-absent.db");
        BuildPreWorkshopSheetsDatabase(dbPath);

        GetTableNames(dbPath).Should().NotContain(WorkshopSheets).And.NotContain(WorkshopSheetItems);

        var journal = new MigrationJournal();
        var result = Prepare(dbPath, journal);

        result.WasAdopted.Should().BeTrue();
        result.AppliedMigrations.Should().Contain(m => m.EndsWith("_AddWorkshopSheets", StringComparison.Ordinal),
            "la migration est réellement exécutée, pas baselinée à tort");
        result.BaselinedMigrations.Should().NotContain(m => m.EndsWith("_AddWorkshopSheets", StringComparison.Ordinal));

        GetTableNames(dbPath).Should().Contain(WorkshopSheets).And.Contain(WorkshopSheetItems);

        using var verify = CreateContext(dbPath);
        verify.Database.GetPendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public void DeuxTablesPresentes_ConsidereLExtensionPresente()
    {
        var dbPath = PathFor("both-present.db");
        BuildAdoptedWorkshopSheetsDatabase(dbPath);

        GetTableNames(dbPath).Should().Contain(WorkshopSheets).And.Contain(WorkshopSheetItems);

        var journal = new MigrationJournal();
        var result = Prepare(dbPath, journal);

        result.WasAdopted.Should().BeTrue();
        result.BaselinedMigrations.Should().Contain(m => m.EndsWith("_AddWorkshopSheets", StringComparison.Ordinal),
            "l'extension est déjà physiquement présente : elle est inscrite, pas ré-exécutée");
        result.AppliedMigrations.Should().NotContain(m => m.EndsWith("_AddWorkshopSheets", StringComparison.Ordinal));

        GetTableNames(dbPath).Should().Contain(WorkshopSheets).And.Contain(WorkshopSheetItems);
    }

    // ===========================================================================================================
    // États partiels — échec explicite, sans fausse entrée d'historique
    // ===========================================================================================================

    [Fact]
    public void RacinePresente_LignesAbsentes_EchoueExplicitement_SansEcrireLHistorique()
    {
        // Baseliner ici marquerait AddWorkshopSheets comme appliquée alors que WorkshopSheetItems n'existe pas :
        // la première génération de fiche échouerait ensuite sur une table manquante.
        var dbPath = PathFor("partial-items-missing.db");
        BuildAdoptedWorkshopSheetsDatabase(dbPath);
        ExecNonQuery(dbPath, $"DROP TABLE \"{WorkshopSheetItems}\"");
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var act = () => Prepare(dbPath, journal);

        act.Should().Throw<DatabaseMigrationException>()
            .Which.Message.Should().Contain(WorkshopSheetItems).And.Contain("partiel");

        GetTableNames(dbPath).Should().NotContain(MigrationsHistory,
            "aucune entrée d'historique ne doit être écrite sur un schéma partiel");
        journal.Entries.Should().Contain(e => e.Contains("partial workshop sheet schema"));
    }

    [Fact]
    public void RacineAbsente_LignesPresentes_EchoueExplicitement_SansTenterLaMigration()
    {
        // Exécuter la migration ici ferait échouer le CreateTable de WorkshopSheetItems (table déjà présente),
        // avec un message technique brut au lieu d'un diagnostic de schéma partiel.
        var dbPath = PathFor("partial-root-missing.db");
        BuildAdoptedWorkshopSheetsDatabase(dbPath);
        ExecNonQuery(dbPath, $"DROP TABLE \"{WorkshopSheets}\"");
        SqliteConnection.ClearAllPools();

        var journal = new MigrationJournal();
        var act = () => Prepare(dbPath, journal);

        act.Should().Throw<DatabaseMigrationException>()
            .Which.Message.Should().Contain(WorkshopSheets).And.Contain("partiel");

        GetTableNames(dbPath).Should().NotContain(MigrationsHistory);
        GetTableNames(dbPath).Should().Contain(WorkshopSheetItems, "aucune table n'est supprimée par le refus");
        journal.Entries.Should().Contain(e => e.Contains("partial workshop sheet schema"));
    }
}
