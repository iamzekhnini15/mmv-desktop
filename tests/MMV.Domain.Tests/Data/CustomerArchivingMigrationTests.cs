using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MMV.Infrastructure.Data;
using Xunit;

namespace MMV.Domain.Tests.Data;

/// <summary>
/// P3-2B — Migration <c>AddCustomerArchivingAndProtectHistory</c> appliquée à une base <b>représentative et
/// peuplée</b> : un client possédant déjà une ordonnance et une vente, créés du temps où les clés étrangères
/// étaient encore <c>Cascade</c> (ordonnances) et <c>SetNull</c> (ventes).
///
/// <para>
/// Prouve que la migration : (1) n'est <b>pas destructive</b> (la reconstruction de table SQLite conserve toutes
/// les lignes et leurs valeurs) ; (2) ajoute <c>Customers.IsArchived</c> avec un <b>backfill</b> à <c>false</c>
/// pour les clients existants ; (3) installe réellement les deux contraintes <c>Restrict</c>, de sorte qu'un
/// historique jusque-là destructible devient protégé <b>au niveau SQL</b>.
/// </para>
/// </summary>
public sealed class CustomerArchivingMigrationTests : IDisposable
{
    /// <summary>Dernière migration AVANT l'archivage client (état de référence : FK Cascade / SetNull).</summary>
    private const string MigrationBeforeArchiving = "20260612071633_AddDocumentSequences";

    /// <summary>Migration P3-2B (archivage + protection de l'historique).</summary>
    private const string ArchivingMigrationSuffix = "_AddCustomerArchivingAndProtectHistory";

    private readonly string _workDirectory;

    public CustomerArchivingMigrationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p32b-migration-" + Guid.NewGuid().ToString("N"));
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
            .UseSqlite($"Data Source={databasePath};Pooling=False").Options;
        return new OpticDbContext(options);
    }

    /// <summary>
    /// Amène la base à l'état d'AVANT P3-2B, puis y insère (en SQL brut, au schéma de l'époque) un client, une
    /// ordonnance et une vente rattachés.
    /// </summary>
    private void BuildPopulatedPreArchivingDatabase(string dbPath, DateTime timestamp)
    {
        using (var context = CreateContext(dbPath))
        {
            context.GetService<IMigrator>().Migrate(MigrationBeforeArchiving);
        }
        SqliteConnection.ClearAllPools();

        ExecNonQuery(dbPath,
            "INSERT INTO \"Customers\" (\"CustomerId\", \"FirstName\", \"LastName\", \"CreatedAt\", \"UpdatedAt\") " +
            "VALUES (1, 'Jean', 'Historique', $timestamp, $timestamp)",
            timestamp);

        // P4-5D : IssueDate est une DATE CIVILE (DateOnly) depuis ADR-PROD-DB-004 §5 décision 7. Le type de
        // colonne SQLite ne change pas — il reste TEXT — mais le FORMAT des valeurs change :
        // « yyyy-MM-dd HH:mm:ss.fffffff » → « yyyy-MM-dd ». Cette fixture écrit donc désormais le format
        // civil, comme le fera toute base après la reprise de données.
        //
        // <b>La reprise elle-même n'existe pas encore</b> : elle est l'obligation T5/T8, explicitement hors
        // périmètre de P4-5D, et son absence est PROUVÉE — pas supposée — par
        // LegacyCivilDateFormatMigrationTests, qui montre qu'une base au format historique échoue à la
        // lecture. Voir ADR-PROD-DB-004 §7.2 : c'est le point le plus dangereux de cet ADR.
        ExecNonQuery(dbPath,
            "INSERT INTO \"Prescriptions\" (\"PrescriptionId\", \"CustomerId\", \"IssueDate\", \"CreatedAt\") " +
            "VALUES (1, 1, $issueDate, $timestamp)",
            timestamp,
            DateOnly.FromDateTime(timestamp).ToString("yyyy-MM-dd"));

        ExecNonQuery(dbPath,
            "INSERT INTO \"Sales\" (\"SaleId\", \"SaleNumber\", \"CustomerId\", \"SaleDate\", \"TotalAmount\", " +
            "\"FinalAmount\", \"PaymentMethod\", \"PaymentStatus\", \"Status\") " +
            "VALUES (1, 'VTE-LEGACY-0001', 1, $timestamp, 250.0, 250.0, 'Cash', 'Paid', 'Delivered')",
            timestamp);

        SqliteConnection.ClearAllPools();
    }

    private static void ExecNonQuery(string dbPath, string sql, DateTime? timestamp = null, string? issueDate = null)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (timestamp.HasValue)
        {
            command.Parameters.AddWithValue("$timestamp", timestamp.Value);
        }

        if (issueDate is not null)
        {
            // Passé en CHAÎNE, délibérément : c'est la représentation civile « yyyy-MM-dd » telle qu'elle
            // sera stockée, et non un DateTime que le pilote reformaterait à sa façon (P4-5D).
            command.Parameters.AddWithValue("$issueDate", issueDate);
        }

        command.ExecuteNonQuery();
    }

    /// <summary>Lit le <c>ON DELETE</c> réel d'une clé étrangère (PRAGMA foreign_key_list).</summary>
    private static string OnDeleteBehavior(string dbPath, string table, string principalTable)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Colonnes : id, seq, table, from, to, on_update, on_delete, match
            if (string.Equals(reader.GetString(2), principalTable, StringComparison.OrdinalIgnoreCase))
            {
                return reader.GetString(6);
            }
        }

        return "(absente)";
    }

    private static bool ColumnExists(string dbPath, string table, string column)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------
    // (1) État initial : la base d'avant P3-2B est bien destructrice
    // ------------------------------------------------------------------

    [Fact]
    public void BeforeMigration_ForeignKeys_AreCascadeAndSetNull_AndArchivingColumnIsAbsent()
    {
        var dbPath = PathFor("before.db");
        BuildPopulatedPreArchivingDatabase(dbPath, DateTime.UtcNow);

        ColumnExists(dbPath, "Customers", "IsArchived").Should().BeFalse(
            "la colonne d'archivage n'existe pas avant P3-2B");
        OnDeleteBehavior(dbPath, "Prescriptions", "Customers").Should().Be("CASCADE",
            "avant P3-2B, supprimer un client détruisait ses ordonnances");
        OnDeleteBehavior(dbPath, "Sales", "Customers").Should().Be("SET NULL",
            "avant P3-2B, supprimer un client anonymisait ses ventes");
    }

    // ------------------------------------------------------------------
    // (2) La migration est non destructive et installe la protection
    // ------------------------------------------------------------------

    [Fact]
    public void Migration_PreservesExistingRows_BackfillsIsArchived_AndInstallsRestrictForeignKeys()
    {
        var dbPath = PathFor("migrate.db");
        var timestamp = DateTime.UtcNow;
        BuildPopulatedPreArchivingDatabase(dbPath, timestamp);

        using (var context = CreateContext(dbPath))
        {
            context.Database.Migrate();
        }
        SqliteConnection.ClearAllPools();

        // (a) Migration réellement appliquée, aucune dérive de modèle restante.
        using (var verify = CreateContext(dbPath))
        {
            verify.Database.GetAppliedMigrations().Should()
                .Contain(m => m.EndsWith(ArchivingMigrationSuffix, StringComparison.Ordinal));
            verify.Database.GetPendingMigrations().Should().BeEmpty();

            // (b) Non destructive : les trois lignes préexistantes sont conservées, valeurs intactes.
            var customer = verify.Customers.AsNoTracking().Should().ContainSingle().Subject;
            customer.FirstName.Should().Be("Jean");
            customer.LastName.Should().Be("Historique");

            // (c) Backfill : un client existant est actif, pas archivé.
            customer.IsArchived.Should().BeFalse("le backfill met les clients existants à IsArchived = false");

            verify.Prescriptions.AsNoTracking().Should().ContainSingle(p => p.CustomerId == customer.CustomerId);

            var sale = verify.Sales.AsNoTracking().Should().ContainSingle().Subject;
            sale.CustomerId.Should().Be(customer.CustomerId, "la vente reste rattachée à son client");
            sale.FinalAmount.Should().Be(250.0m, "la reconstruction de table conserve les montants");
            sale.SaleNumber.Should().Be("VTE-LEGACY-0001");
        }
        SqliteConnection.ClearAllPools();

        // (d) Les deux contraintes sont désormais physiquement en Restrict.
        OnDeleteBehavior(dbPath, "Prescriptions", "Customers").Should().Be("RESTRICT");
        OnDeleteBehavior(dbPath, "Sales", "Customers").Should().Be("RESTRICT");

        // (e) Preuve fonctionnelle : l'historique jusque-là destructible est maintenant protégé par la base.
        using var context2 = CreateContext(dbPath);
        var target = context2.Customers.Single();
        context2.Customers.Remove(target);

        Action act = () => context2.SaveChanges();

        act.Should().Throw<DbUpdateException>(
            "après migration, la base refuse de supprimer un client porteur d'historique");
    }
}
