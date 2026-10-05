using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using MMV.DatabaseManager.Import;
using MMV.DatabaseManager.Tests.TestDoubles;
using MMV.Import.TestSupport;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Tests.Import;

/// <summary>
/// P4-7 — ordre normatif de <see cref="SqliteImportRunner"/> jusqu'à la transaction, sans serveur : chaque refus
/// de la source, des options, de la sauvegarde, du verrou et de la cible est prononcé <b>avant toute écriture</b>, et
/// les refus de la source avant <b>tout contact serveur</b> (journal d'appels vide). La source n'est jamais modifiée.
/// La transaction elle-même est prouvée contre un vrai PostgreSQL (tests d'intégration <c>Import/</c>).
/// </summary>
public sealed class SqliteImportRunnerTests : IDisposable
{
    private static readonly ImportPlan Plan = ImportOptionsAndPlanTests.PostgreSqlPlan();

    private readonly SqliteSourceBuilder _source = SqliteSourceBuilder.CreateMigrated();
    private readonly string _directory = Directory.CreateTempSubdirectory("mmv-import-unit-").FullName;
    private readonly RecordingPorts _ports = new() { Known = ["B"], Applied = ["B"] };

    public SqliteImportRunnerTests() => _source.Populate(3);

    public void Dispose()
    {
        _source.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private async Task<(SqliteImportResult Result, JsonElement? Report)> RunAsync(
        string? source = null, string zone = "Europe/Paris", string? report = null)
    {
        var reportPath = report ?? Path.Combine(_directory, $"r-{Guid.NewGuid():N}.json");
        var trace = new MigrationJournal();
        var runner = new SqliteImportRunner(_ports.Ports, new SqliteConnection(), Plan, _ports.Backup, trace,
            _ => throw new InvalidOperationException("aucune connexion neuve avant la transaction"));

        var result = await runner.RunAsync(new SqliteImportRequest(
            source ?? _source.Path, zone, "OP-U", "backup.manifest.json", reportPath, false, TimeSpan.Zero, ApplicationVersion.Current));

        trace.Entries.Should().Contain(e => e.Contains(" OPEN import ", StringComparison.Ordinal))
            .And.Contain(e => e.Contains(" CLOSE import ", StringComparison.Ordinal));
        var text = File.Exists(reportPath) ? await File.ReadAllTextAsync(reportPath) : string.Empty;
        return (result, text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : null);
    }

    private async Task<(SqliteImportResult Result, JsonElement Report)> RejectedAsync()
    {
        var (hash, _) = await SqliteImportSource.HashAsync(_source.Path);

        var (result, report) = await RunAsync();

        result.ExitCode.Should().Be(MigrationExitCode.ImportSourceRejected, result.Message);
        result.Message.Should().Contain("aucune écriture");
        _ports.Calls.Should().BeEmpty("une source refusée l'est avant tout contact serveur");
        (await SqliteImportSource.HashAsync(_source.Path)).Sha256.Should().Be(hash, "la source est ouverte en lecture seule");
        report!.Value.GetProperty("Outcome").GetString().Should().Be("source-rejected");
        return (result, report.Value);
    }

    [Fact]
    public async Task Valid_source_passes_every_source_check_and_then_requires_a_verified_backup()
    {
        _ports.BackupVerified = false;

        var (result, report) = await RunAsync();

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified, result.Message);
        _ports.Calls.Should().Equal("backup.verify");
        report!.Value.GetProperty("RejectedValueCount").GetInt64().Should().Be(0);
        report.Value.GetProperty("Tables").GetArrayLength().Should().Be(21);
        report.Value.GetProperty("SourceSha256").GetString().Should().HaveLength(64);
        File.Exists(_source.Path + "-journal").Should().BeFalse("aucun journal créé : lecture seule");
    }

    [Fact]
    public async Task Missing_or_unclosed_source_file_is_refused()
    {
        var (missing, _) = await RunAsync(Path.Combine(_directory, "absent.db"));
        missing.ExitCode.Should().Be(MigrationExitCode.ImportSourceRejected);
        missing.Message.Should().Contain("introuvable");

        await File.WriteAllTextAsync(_source.Path + "-journal", "transaction interrompue");
        var (hot, _) = await RunAsync();
        hot.ExitCode.Should().Be(MigrationExitCode.ImportSourceRejected);
        hot.Message.Should().Contain("copie à froid");
        _ports.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Corrupted_source_is_refused()
    {
        var bytes = await File.ReadAllBytesAsync(_source.Path);
        Array.Fill(bytes, (byte)0xA5, 4096, 4096);
        await File.WriteAllBytesAsync(_source.Path, bytes);

        var (result, _) = await RunAsync();

        result.ExitCode.Should().Be(MigrationExitCode.ImportSourceRejected, result.Message);
        _ports.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Source_with_a_different_migration_history_is_refused()
    {
        _source.Execute("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = (SELECT max(\"MigrationId\") FROM \"__EFMigrationsHistory\")");

        var (result, _) = await RejectedAsync();

        result.Message.Should().Contain("historique SQLite").And.Contain("version courante de MMV");
    }

    [Fact]
    public async Task Orphan_rows_are_refused_by_the_source_foreign_key_check()
    {
        _source.Execute("UPDATE \"SaleItems\" SET \"SaleId\" = 999999 WHERE rowid = (SELECT min(rowid) FROM \"SaleItems\")", foreignKeys: false);

        var (result, _) = await RejectedAsync();

        result.Message.Should().Contain("orpheline").And.Contain("SaleItems");
    }

    [Fact]
    public async Task Unknown_table_holding_data_is_refused_but_an_empty_one_is_excluded_and_reported()
    {
        _source.Execute("CREATE TABLE \"LegacyNotes\" (\"Id\" INTEGER PRIMARY KEY, \"Text\" TEXT)");
        _ports.BackupVerified = false;

        var (empty, report) = await RunAsync();
        empty.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified, empty.Message);
        report!.Value.GetProperty("ExcludedSourceTables").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(e => e!.StartsWith("LegacyNotes", StringComparison.Ordinal));

        _ports.Calls.Clear();
        _source.Execute("INSERT INTO \"LegacyNotes\" (\"Text\") VALUES ('note')");
        var (result, _) = await RejectedAsync();
        result.Message.Should().Contain("LegacyNotes").And.Contain("ne seraient pas importées");
    }

    [Fact]
    public async Task Source_column_unknown_to_the_model_is_refused()
    {
        _source.Execute("ALTER TABLE \"Suppliers\" ADD COLUMN \"Fax\" TEXT");

        var (result, _) = await RejectedAsync();

        result.Message.Should().Contain("colonnes de 'Suppliers'");
    }

    [Theory]
    [InlineData("UPDATE \"Users\" SET \"Role\" = 'Superman'", "Users", "Role")]
    [InlineData("UPDATE \"Suppliers\" SET \"Name\" = printf('%.201c', 'x')", "Suppliers", "Name")]
    [InlineData("UPDATE \"Customers\" SET \"BirthDate\" = '15/03/1985'", "Customers", "BirthDate")]
    [InlineData("UPDATE \"Users\" SET \"CreatedAt\" = '2026-03-29 02:30:00'", "Users", "CreatedAt")]
    [InlineData("UPDATE \"Users\" SET \"IsActive\" = 7", "Users", "IsActive")]
    [InlineData("UPDATE \"Sales\" SET \"TotalAmount\" = 'beaucoup'", "Sales", "TotalAmount")]
    public async Task Every_non_importable_value_is_located_in_the_report_and_nothing_is_written(string sql, string table, string column)
    {
        _source.Execute(sql);

        var (result, report) = await RejectedAsync();

        result.Message.Should().Contain("non importable");
        report.GetProperty("RejectedValueCount").GetInt64().Should().Be(3, "les trois lignes, pas seulement la première");
        report.GetProperty("RejectedValues").EnumerateArray()
            .Should().OnlyContain(f => f.GetProperty("Table").GetString() == table && f.GetProperty("Column").GetString() == column);
    }

    [Fact]
    public async Task Unknown_time_zone_or_existing_report_is_refused_before_anything_else()
    {
        var (zone, _) = await RunAsync(zone: "Mars/Olympus_Mons");
        zone.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        zone.Message.Should().Contain("Fuseau");

        var existing = Path.Combine(_directory, "existing.json");
        await File.WriteAllTextAsync(existing, "rapport précédent");
        var (report, _) = await RunAsync(report: existing);
        report.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        (await File.ReadAllTextAsync(existing)).Should().Be("rapport précédent", "un rapport n'est jamais écrasé");
        _ports.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Busy_lock_refuses_without_any_write()
    {
        _ports.LockAvailable = false;

        var (result, report) = await RunAsync();

        result.ExitCode.Should().Be(MigrationExitCode.LockNotAcquired);
        _ports.Calls.Should().Equal("backup.verify", "lock.acquire");
        report!.Value.GetProperty("ExitCode").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task Backup_that_is_not_the_current_state_of_the_target_is_refused_under_the_lock_without_any_write()
    {
        _ports.BackupMismatch = "la base a changé depuis la sauvegarde";

        var (result, _) = await RunAsync();

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified);
        _ports.Calls.Should().Equal("backup.verify", "lock.acquire", "state.read", "backup.confirm", "lock.release");
    }

    [Fact]
    public async Task Target_not_up_to_date_or_without_compatibility_metadata_is_refused_without_any_write()
    {
        _ports.Applied = [];
        var (outdated, _) = await RunAsync();
        outdated.ExitCode.Should().Be(MigrationExitCode.ImportTargetRefused);
        outdated.Message.Should().Contain("exécuter migrate");

        _ports.Calls.Clear();
        _ports.Applied = ["B"];
        _ports.Metadata = ServerCompatibilityMetadata.NoRow;
        var (bare, _) = await RunAsync();
        bare.ExitCode.Should().Be(MigrationExitCode.ImportTargetRefused);
        _ports.Calls.Should().NotIntersectWith(RecordingPorts.WriteCalls).And.EndWith("lock.release");
    }
}
