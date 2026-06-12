using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Persistence;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Persistence;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P2A-1E — Sûreté de la migration <c>AddDocumentSequences</c> sur les bases <b>antérieures à P2A-1E</b>
/// (R-03 / ADR-006).
///
/// Prouve, sur un <b>vrai SQLite temporaire</b>, qu'une base historique réaliste créée APRÈS le correctif
/// R-19 mais AVANT P2A-1E (donc <b>sans</b> table <c>DocumentSequences</c>, et sans les anciens DEFAULT
/// DateTime) est <b>adoptée sans perte</b> : la table de numérotation est <b>physiquement créée</b> par
/// exécution de la migration (et non baselinée à tort), les compteurs <c>SALE</c>/<c>ORDER</c> sont seedés,
/// aucune migration ne reste en attente, et un numéro peut être attribué immédiatement.
/// </summary>
public sealed class DocumentSequencesAdoptionTests : IDisposable
{
    /// <summary>Dernière migration AVANT P2A-1E : son schéma ne contient pas DocumentSequences.</summary>
    private const string MigrationBeforeNumbering = "20260611114307_FixDateTimeDefaultValues";

    private readonly string _workDirectory;

    public DocumentSequencesAdoptionTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p2a1e-adopt-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Construit une base « historique » antérieure à P2A-1E : migrée jusqu'à FixDateTimeDefaultValues
    /// (schéma sans DocumentSequences), peuplée d'un client, puis privée de <c>__EFMigrationsHistory</c>
    /// pour simuler une base <c>EnsureCreated</c> d'un client installé entre R-19 et P2A-1E.
    /// </summary>
    private void BuildPreNumberingHistoricalDatabase(string dbPath, string firstName, string lastName)
    {
        using (var context = CreateContext(dbPath))
        {
            context.GetService<IMigrator>().Migrate(MigrationBeforeNumbering);
            context.Customers.Add(new Customer { FirstName = firstName, LastName = lastName });
            context.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        ExecNonQuery(dbPath, "DROP TABLE \"__EFMigrationsHistory\"");
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Adopt_PreNumberingHistoricalDatabase_CreatesDocumentSequencesTable_AndAllocatesNumber()
    {
        var dbPath = PathFor("historical-pre-p2a1e.db");
        BuildPreNumberingHistoricalDatabase(dbPath, "Avant", "Numerotation");

        // Pré-condition : la table de numérotation est ABSENTE et aucun historique de migration n'existe.
        GetTableNames(dbPath).Should().NotContain("DocumentSequences", "base antérieure à P2A-1E");
        GetTableNames(dbPath).Should().NotContain("__EFMigrationsHistory");

        var journal = new MigrationJournal();
        var manager = new SqliteDatabaseManager(journal);

        DatabasePreparationResult result;
        using (var context = CreateContext(dbPath))
        {
            result = manager.PrepareDatabase(context);
        }
        SqliteConnection.ClearAllPools();

        // Adoptée ; AddDocumentSequences EXÉCUTÉE (et non baselinée à tort).
        result.WasAdopted.Should().BeTrue();
        result.AppliedMigrations.Should().Contain(m => m.EndsWith("_AddDocumentSequences", StringComparison.Ordinal),
            "la migration de numérotation est réellement appliquée, pas seulement inscrite");
        result.BaselinedMigrations.Should().NotContain(m => m.EndsWith("_AddDocumentSequences", StringComparison.Ordinal));
        journal.Entries.Should().Contain(e => e.Contains("AddDocumentSequences will be executed"));

        // La table de numérotation existe physiquement et les compteurs sont seedés.
        GetTableNames(dbPath).Should().Contain("DocumentSequences");

        using (var verify = CreateContext(dbPath))
        {
            verify.Database.GetPendingMigrations().Should().BeEmpty();
            verify.Database.HasPendingModelChanges().Should().BeFalse();

            verify.DocumentSequences.AsNoTracking().Select(s => s.SequenceName)
                .Should().Contain(new[] { DocumentSequenceNames.Sale, DocumentSequenceNames.Order });

            // Données conservées (adoption non destructive).
            verify.Customers.Should().ContainSingle(c => c.FirstName == "Avant" && c.LastName == "Numerotation");
        }

        // Un numéro peut être attribué immédiatement après adoption.
        using (var context = CreateContext(dbPath))
        {
            var service = new EfNumberSequenceService(context);
            service.NextNumberAsync(DocumentSequenceNames.Sale).GetAwaiter().GetResult().Should().Be("VTE-000001");
        }
    }
}
