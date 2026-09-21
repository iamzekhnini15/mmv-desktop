using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.Data.Time;
using Xunit;

namespace MMV.Domain.Tests.Data.Time;

/// <summary>
/// P4-5D-R — <b>la reprise dans le cycle de vie réel de la base</b>, sur de vrais fichiers.
///
/// <para>
/// <b>Le point décisif de ce fichier.</b> Toutes les réparations antérieures (R-19, P2A-1E, P3-2B…) sont
/// portées par le chemin d'<i>adoption</i>, celui des bases historiques sans <c>__EFMigrationsHistory</c>.
/// La reprise des dates civiles ne peut pas s'y limiter : le passage à <see cref="DateOnly"/> n'a produit
/// <b>aucune</b> migration, et la base la plus répandue en exploitation — installée après P2A-1A, avant
/// P4-5D — est <b>déjà gérée par migrations</b> et n'emprunte donc jamais ce chemin. Le premier test
/// ci-dessous est celui qui aurait échoué si la reprise avait été rattachée à l'adoption.
/// </para>
///
/// <para>
/// Tous les tests utilisent des fichiers temporaires isolés, jamais la base utilisateur réelle.
/// <b>Aucune migration EF n'est créée ni modifiée.</b>
/// </para>
/// </summary>
public sealed class CivilDateRepairPreparationTests : IDisposable
{
    private readonly string _workDirectory;

    public CivilDateRepairPreparationTests()
    {
        _workDirectory = Path.Combine(Path.GetTempPath(), "mmv-p45dr-" + Guid.NewGuid().ToString("N"));
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
        => new(new DbContextOptionsBuilder<OpticDbContext>()
            .UseSqlite($"Data Source={databasePath};Pooling=False")
            .Options);

    /// <summary>
    /// Construit une base <b>gérée par migrations</b> (cas nominal d'une installation en service), puis y
    /// écrit en SQL brut une date de naissance au format historique — ce que le modèle courant ne sait plus
    /// écrire.
    /// </summary>
    private void CreateMigratedDatabaseWithLegacyCustomer(string databasePath, string birthDateLiteral)
    {
        using (var context = CreateContext(databasePath))
        {
            context.Database.Migrate();
        }

        SqliteConnection.ClearAllPools();

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"BirthDate\", \"IsArchived\", " +
            "\"CreatedAt\", \"UpdatedAt\") VALUES ('Jean', 'Historique', $birthDate, 0, $ts, $ts)";
        command.Parameters.AddWithValue("$birthDate", birthDateLiteral);
        command.Parameters.AddWithValue("$ts", "2026-01-01 00:00:00");
        command.ExecuteNonQuery();
    }

    private static string? ReadRawBirthDate(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(\"BirthDate\" AS TEXT) FROM \"Customers\" LIMIT 1";
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : (string)value;
    }

    private static string Sha256(string value)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // =====================================================================
    // (1) Le cas que l'adoption n'aurait jamais couvert
    // =====================================================================

    [Fact]
    public void UneBaseGereeParMigrations_MaisAnterieureAP45D_EstRepriseAuDemarrage()
    {
        var databasePath = PathFor("managed-legacy.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15 00:00:00");

        var manager = new SqliteDatabaseManager();
        using (var context = CreateContext(databasePath))
        {
            var result = manager.PrepareDatabase(context);

            result.DetectedState.Should().Be(DatabaseState.MigrationsManaged,
                "aucune migration n'est en attente : c'est bien le chemin « déjà gérée » qui est emprunté");
            result.AppliedMigrations.Should().BeEmpty("le schéma n'a pas changé — seules les VALEURS l'ont");
            result.CivilDateRepair.Should().NotBeNull();
            result.CivilDateRepair!.TotalTruncatedRows.Should().Be(1);
        }

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15");

        using var after = CreateContext(databasePath);
        after.Customers.Single().BirthDate.Should().Be(new DateOnly(1985, 3, 15));
    }

    [Fact]
    public void LaPreparation_SauvegardeLaBase_AvantDeLaReprendre()
    {
        // La sauvegarde de fichier précède toute mutation : c'est elle qui rend la reprise réversible. Le
        // service de reprise ne copie pas de fichier — il s'appuie sur cette garantie du cycle de vie.
        var databasePath = PathFor("backup-before-repair.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15 00:00:00");

        var manager = new SqliteDatabaseManager();
        string backupPath;
        using (var context = CreateContext(databasePath))
        {
            backupPath = manager.PrepareDatabase(context).BackupPath!;
        }

        backupPath.Should().NotBeNull();
        File.Exists(backupPath).Should().BeTrue();

        // La sauvegarde porte encore le format historique : elle est bien antérieure à la reprise.
        ReadRawBirthDate(backupPath).Should().Be("1985-03-15 00:00:00");
        ReadRawBirthDate(databasePath).Should().Be("1985-03-15");
    }

    [Fact]
    public void LaSauvegarde_PermetDeRevenirEnArriere()
    {
        // Le rollback n'est pas une intention : il est exécuté ici. Restaurer la sauvegarde rend la base à
        // son état d'origine — reprise comprise.
        var databasePath = PathFor("rollback.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15 00:00:00");

        var manager = new SqliteDatabaseManager();
        string backupPath;
        using (var context = CreateContext(databasePath))
        {
            backupPath = manager.PrepareDatabase(context).BackupPath!;
        }

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15");

        SqliteConnection.ClearAllPools();
        manager.Restore(backupPath, databasePath);

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15 00:00:00",
            "la restauration ramène exactement l'état d'avant la reprise");
    }

    [Fact]
    public void UneBaseNeuve_NAFaireAucuneReprise()
    {
        var databasePath = PathFor("fresh.db");

        using var context = CreateContext(databasePath);
        var result = new SqliteDatabaseManager().PrepareDatabase(context);

        result.WasFreshInstall.Should().BeTrue();
        result.CivilDateRepair.Should().NotBeNull();
        result.CivilDateRepair!.TotalRewrittenRows.Should().Be(0);
        result.CivilDateRepair.AuditBefore.RequiresRepair.Should().BeFalse();
    }

    [Fact]
    public void UneBaseDejaReprise_EstLaisseeTelleQuelle()
    {
        // Rejouabilité au niveau du démarrage : deux lancements successifs de l'application ne produisent
        // qu'une seule reprise.
        var databasePath = PathFor("idempotent.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15 12:30:45");

        var manager = new SqliteDatabaseManager();
        using (var context = CreateContext(databasePath))
        {
            manager.PrepareDatabase(context).CivilDateRepair!.TotalRewrittenRows.Should().Be(1);
        }

        using (var context = CreateContext(databasePath))
        {
            manager.PrepareDatabase(context).CivilDateRepair!.TotalRewrittenRows.Should().Be(0);
        }

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15");
    }

    // =====================================================================
    // (2) Refus : on échoue bruyamment plutôt que de reprendre à moitié
    // =====================================================================

    [Fact]
    public void UneValeurNonReparable_FaitEchouerLaPreparation_SansRienModifier()
    {
        // Le refus est en bloc et AVANT toute écriture : une base à moitié reprise, sans trace de ce qui
        // reste à faire, serait le pire des états. La valeur saine voisine n'est donc pas réparée non plus.
        var databasePath = PathFor("unrepairable.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15 00:00:00");

        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"BirthDate\", \"IsArchived\", " +
                "\"CreatedAt\", \"UpdatedAt\") VALUES ('Zoe', 'Illisible', 'pas-une-date', 0, " +
                "'2026-01-01 00:00:00', '2026-01-01 00:00:00')";
            command.ExecuteNonQuery();
        }

        var manager = new SqliteDatabaseManager();
        using (var context = CreateContext(databasePath))
        {
            var act = () => manager.PrepareDatabase(context);

            act.Should().Throw<DatabaseMigrationException>()
                .WithMessage("*dates civiles*")
                .WithMessage("*AUCUNE modification*")
                .WithMessage($"*BirthDateHash={Sha256("pas-une-date")}*",
                    "la valeur fautive est désignée pour le support par sa clé et son empreinte (D-B3)")
                .Which.Message.Should().NotContain("pas-une-date", "jamais en clair (D-B3)");
        }

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15 00:00:00",
            "aucune ligne n'est reprise tant qu'une valeur reste non arbitrée");

        manager.Journal.Entries.Should().Contain(e => e.Contains("CIVILDATE REFUSED"));
    }

    [Fact]
    public void UnRefus_ConserveLaBaseEtSaSauvegarde()
    {
        var databasePath = PathFor("refused-keeps-backup.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "2026-02-30");

        var manager = new SqliteDatabaseManager();
        using (var context = CreateContext(databasePath))
        {
            var act = () => manager.PrepareDatabase(context);
            act.Should().Throw<DatabaseMigrationException>();
        }

        File.Exists(databasePath).Should().BeTrue("la base n'est jamais supprimée");
        Directory.GetFiles(Path.Combine(_workDirectory, "backups"))
            .Should().NotBeEmpty("la sauvegarde prise avant mutation est conservée");
        ReadRawBirthDate(databasePath).Should().Be("2026-02-30",
            "une date inexistante au calendrier n'est jamais recalée en silence");
    }

    // =====================================================================
    // (3) Journalisation — la reprise laisse une trace exploitable
    // =====================================================================

    [Fact]
    public void LaReprise_EstJournalisee_AvantEtApres()
    {
        var databasePath = PathFor("journalled.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15 00:00:00");

        var manager = new SqliteDatabaseManager();
        using (var context = CreateContext(databasePath))
        {
            manager.PrepareDatabase(context);
        }

        manager.Journal.Entries.Should().Contain(e => e.Contains("CIVILDATE detected"));
        manager.Journal.Entries.Should().Contain(e => e.Contains("CIVILDATE repaired"));
        manager.Journal.Entries.Should().Contain(e => e.Contains("BACKUP created"));
    }

    [Fact]
    public void UneBaseSaine_LeJournaleAussi()
    {
        var databasePath = PathFor("journalled-clean.db");

        var manager = new SqliteDatabaseManager();
        using var context = CreateContext(databasePath);
        manager.PrepareDatabase(context);

        manager.Journal.Entries.Should().Contain(e => e.Contains("CIVILDATE ok"),
            "l'absence de reprise est un fait à tracer, pas un silence");
    }

    // =====================================================================
    // (4) Base historique (EnsureCreated) : l'autre chemin reste couvert
    // =====================================================================

    [Fact]
    public void UneBaseHistoriqueAdoptee_EstAussiReprise()
    {
        var databasePath = PathFor("historical.db");

        using (var context = CreateContext(databasePath))
        {
            context.Database.EnsureCreated();
        }

        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"Customers\" (\"FirstName\", \"LastName\", \"BirthDate\", \"IsArchived\", " +
                "\"CreatedAt\", \"UpdatedAt\") VALUES ('Jean', 'Historique', '1985-03-15 00:00:00', 0, " +
                "'2026-01-01 00:00:00', '2026-01-01 00:00:00')";
            command.ExecuteNonQuery();
        }

        using (var context = CreateContext(databasePath))
        {
            var result = new SqliteDatabaseManager().PrepareDatabase(context);
            result.WasAdopted.Should().BeTrue();
            result.CivilDateRepair!.TotalTruncatedRows.Should().Be(1);
        }

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15");
    }

    // =====================================================================
    // (5) P4-5D-R Phase C — D-B1 et D-B3 au démarrage réel
    // =====================================================================

    [Fact]
    public void UnSuffixeInconnu_RefuseLeDemarrage_SansJournaliserLaDate()
    {
        // D-B1 : « 1985-03-15garbage » était repris avant cette phase ; il refuse désormais le démarrage (Q2).
        // D-B3 : ni le journal en mémoire, ni le FICHIER journal, ni le message d'erreur ne portent la date.
        var databasePath = PathFor("refused-redacted.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "1985-03-15garbage");
        var journalPath = PathFor("migration.log");

        var manager = new SqliteDatabaseManager(new MigrationJournal(journalPath));
        DatabaseMigrationException refusal;
        using (var context = CreateContext(databasePath))
        {
            var act = () => manager.PrepareDatabase(context);
            refusal = act.Should().Throw<DatabaseMigrationException>().Which;
        }

        var customerId = ReadSingleCustomerId(databasePath);
        var designation = $"Customers.BirthDate CustomerId={customerId} BirthDateHash={Sha256("1985-03-15garbage")}";

        manager.Journal.Entries.Single(e => e.Contains("CIVILDATE REFUSED"))
            .Should().Contain("1 unrepairable value(s)").And.Contain(designation);
        refusal.Message.Should().Contain(designation);

        manager.Journal.Entries.Should().NotContain(e => e.Contains("1985-03-15"));
        File.ReadAllText(journalPath).Should().NotContain("1985-03-15");
        refusal.Message.Should().NotContain("1985-03-15");

        ReadRawBirthDate(databasePath).Should().Be("1985-03-15garbage", "refus en bloc : rien n'est écrit");
    }

    [Fact]
    public void LeJournalDeRefus_PermetDeRetrouverEtDeConfirmerChaqueValeur()
    {
        // « Possibilité de diagnostic » : sur les deux colonnes, la clé désigne la ligne, et l'empreinte de la
        // valeur lue en base par cette clé est celle du journal. Le support confirme sans que la date circule.
        var databasePath = PathFor("refused-diagnosable.db");
        CreateMigratedDatabaseWithLegacyCustomer(databasePath, "15/03/1985");
        var customerId = ReadSingleCustomerId(databasePath);

        long prescriptionId;
        SqliteConnection.ClearAllPools();
        using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO \"Prescriptions\" (\"CustomerId\", \"IssueDate\", \"CreatedAt\") " +
                "VALUES ($customerId, '20/01/2026', '2026-01-20 00:00:00'); SELECT last_insert_rowid()";
            command.Parameters.AddWithValue("$customerId", customerId);
            prescriptionId = (long)command.ExecuteScalar()!;
        }

        var manager = new SqliteDatabaseManager();
        using (var context = CreateContext(databasePath))
        {
            var act = () => manager.PrepareDatabase(context);
            act.Should().Throw<DatabaseMigrationException>();
        }

        var refusedLine = manager.Journal.Entries.Single(e => e.Contains("CIVILDATE REFUSED"));
        refusedLine.Should().Contain("2 unrepairable value(s)")
            .And.Contain($"Customers.BirthDate CustomerId={customerId} BirthDateHash=")
            .And.Contain($"Prescriptions.IssueDate PrescriptionId={prescriptionId} IssueDateHash=")
            .And.NotContain("15/03/1985")
            .And.NotContain("20/01/2026");

        // Le support relit la ligne par sa clé et retrouve l'empreinte journalisée.
        refusedLine.Should().Contain("BirthDateHash=" + Sha256(ReadRawBirthDate(databasePath)!));
        refusedLine.Should().Contain("IssueDateHash=" + Sha256(ReadRawIssueDate(databasePath, prescriptionId)));
    }

    private static long ReadSingleCustomerId(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"CustomerId\" FROM \"Customers\"";
        return (long)command.ExecuteScalar()!;
    }

    private static string ReadRawIssueDate(string databasePath, long prescriptionId)
    {
        SqliteConnection.ClearAllPools();
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CAST(\"IssueDate\" AS TEXT) FROM \"Prescriptions\" WHERE \"PrescriptionId\" = $id";
        command.Parameters.AddWithValue("$id", prescriptionId);
        return (string)command.ExecuteScalar()!;
    }
}
