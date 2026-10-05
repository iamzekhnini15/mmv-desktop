using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Import;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Locking;
using MMV.Import.TestSupport;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Import;

/// <summary>
/// P4-7 — import SQLite → PostgreSQL contre un <b>vrai</b> PostgreSQL : base cible neuve, propriété d'un rôle
/// migrateur, migrée par l'outil réel (chaîne de production) ; source SQLite migrée par la chaîne SQLite de
/// production. Seule la vérification de sauvegarde est un double (seam du runner) : la preuve réelle P4-9 est
/// exercée de bout en bout par <see cref="SqliteImportEndToEndTests"/>. Chaque propriété est vérifiée par une
/// <b>lecture indépendante</b> des deux bases, jamais par le seul rapport de l'outil.
/// </summary>
public sealed class SqliteImportTests : IAsyncLifetime
{
    private const string Zone = "Europe/Paris";

    private readonly string _directory = Directory.CreateTempSubdirectory("mmv-it-import-").FullName;
    private LifecycleDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _db = await LifecycleDatabase.CreateAsync();
        (await _db.RunAsync<ChainS1>(ApplicationVersion.Current)).ExitCode.Should().Be(MigrationExitCode.Success);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    // ---- outil -----------------------------------------------------------------------------------------------

    private sealed record Run(SqliteImportResult Result, JsonElement Report, string ReportText);

    private async Task<Run> ImportAsync(
        string source,
        bool dryRun = false,
        string zone = Zone,
        Func<string, CancellationToken, Task>? afterTable = null,
        LifecycleDatabase? target = null)
    {
        var db = target ?? _db;
        await using var session = db.Session<ChainS1>();
        var runner = new SqliteImportRunner(session.Ports, session.Connection, ImportPlan.From(session.DesignTimeModel),
            new AcceptingBackupVerification(), new MigrationJournal(),
            async cancellationToken =>
            {
                var fresh = new NpgsqlConnection(db.MigratorConnectionString);
                await fresh.OpenAsync(cancellationToken);
                return fresh;
            },
            afterTable);
        var reportPath = Path.Combine(_directory, $"import-{Guid.NewGuid():N}.json");

        var result = await runner.RunAsync(new SqliteImportRequest(
            source, zone, "OP-IT", "backup-it", reportPath, dryRun, TimeSpan.Zero, ApplicationVersion.Current));

        var text = await File.ReadAllTextAsync(reportPath);
        text.Should().NotContain("mmv_it_ephemeral", "aucun secret dans le rapport");
        return new Run(result, JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private static readonly string[] Tables =
    [
        "AccessoryDetails", "Customers", "DocumentSequences", "GlassDetails", "GlassPricingTiers", "GlassSupplements",
        "LensDetails", "Notifications", "OrderItems", "Orders", "Prescriptions", "ProductCategories", "Products",
        "SaleItems", "Sales", "StockMovements", "Supplements", "Suppliers", "Users", "WorkshopSheetItems", "WorkshopSheets"
    ];

    private Task<long> PgCountAsync(string table, LifecycleDatabase? db = null) =>
        (db ?? _db).AdminScalarAsync<long>($"SELECT count(*) FROM \"{table}\"");

    private async Task<Dictionary<string, long>> PgCountsAsync(LifecycleDatabase? db = null)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in Tables)
        {
            counts[table] = await PgCountAsync(table, db);
        }

        return counts;
    }

    private async Task<List<string>> PgStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_db.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? "∅" : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return values;
    }

    private static Task<Dictionary<string, long>> FreshCountsAsync() =>
        Task.FromResult(Tables.ToDictionary(t => t, t => t == "DocumentSequences" ? 2L : 0L));

    // ---- import nominal -----------------------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task Empty_source_imports_only_the_seed_rows_and_never_touches_the_postgresql_history()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        var historyBefore = await PgStringsAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1");

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        run.Report.GetProperty("Outcome").GetString().Should().Be("imported");
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync());
        (await PgStringsAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1"))
            .Should().Equal(historyBefore, "l'historique EF PostgreSQL n'est jamais alimenté depuis SQLite (O11, K-10)");
        historyBefore.Should().ContainSingle().Which.Should().Contain("InitialPostgreSqlBaseline");
        run.Report.GetProperty("ExcludedSourceTables")[0].GetString().Should().StartWith("__EFMigrationsHistory");
    }

    [PostgreSqlFact]
    public async Task Populated_source_is_imported_with_its_identifiers_values_nulls_and_relations()
    {
        const int rows = 12;
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(rows);

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        foreach (var table in Tables)
        {
            (await PgCountAsync(table)).Should().Be(source.Scalar<long>($"SELECT count(*) FROM \"{table}\""), table);
        }

        // Identifiants conservés (non contigus : 7, 10, 13, …) et relations 1-N / N-N intactes.
        (await PgStringsAsync("SELECT \"SupplierId\" FROM \"Suppliers\" ORDER BY 1"))
            .Should().Equal(Enumerable.Range(0, rows).Select(i => SqliteSourceBuilder.IntegerKey(i).ToString(CultureInfo.InvariantCulture)));
        (await PgStringsAsync("SELECT \"GlassId\" || '-' || \"SupplementId\" FROM \"GlassSupplements\" ORDER BY \"GlassId\""))
            .Should().HaveCount(rows).And.OnlyContain(pair => pair.Split('-', StringSplitOptions.None)[0] == pair.Split('-', StringSplitOptions.None)[1]);
        (await _db.AdminScalarAsync<long>(
                "SELECT count(*) FROM \"SaleItems\" i JOIN \"Sales\" s ON s.\"SaleId\" = i.\"SaleId\""))
            .Should().Be(rows, "chaque ligne de vente référence sa vente importée");

        // Chaînes (non ASCII), énumérations par nom, NULL : colonne par colonne, contre la source.
        foreach (var (table, key, column) in new[]
                 {
                     ("Suppliers", "SupplierId", "Name"), ("Suppliers", "SupplierId", "Address"),
                     ("Users", "UserId", "Role"), ("Orders", "OrderId", "Status")
                 })
        {
            var sourceValues = source.Strings($"SELECT coalesce(\"{column}\", '∅') FROM \"{table}\" ORDER BY \"{key}\"");
            sourceValues.Should().HaveCount(rows);
            (await PgStringsAsync($"SELECT coalesce(\"{column}\", '∅') FROM \"{table}\" ORDER BY \"{key}\""))
                .Should().Equal(sourceValues, $"{table}.{column}");
        }

        (await _db.AdminScalarAsync<long>("SELECT count(*) FROM \"Suppliers\" WHERE \"Address\" IS NULL"))
            .Should().Be(source.Scalar<long>("SELECT count(*) FROM \"Suppliers\" WHERE \"Address\" IS NULL")).And.BeGreaterThan(0);

        // Montants : 0.125, 1.125, … en REAL → numeric(12,2) arrondi au pair : x.12, jamais x.13.
        var amounts = await PgStringsAsync("SELECT \"TotalAmount\"::text FROM \"Sales\" ORDER BY \"SaleId\"");
        amounts.Should().Equal(Enumerable.Range(0, rows).Select(i => ((i % 9) + 0.12m).ToString("F2", CultureInfo.InvariantCulture)));

        // Instants : heure locale de Paris (été, UTC+2) → UTC, microseconde.
        (await _db.AdminScalarAsync<DateTime>("SELECT min(\"CreatedAt\") FROM \"Users\""))
            .Should().Be(new DateTime(2026, 6, 15, 8, 30, 0, DateTimeKind.Utc).AddTicks(1234560));

        // Dates civiles : forme canonique et forme historique tronquée → la même date.
        (await PgStringsAsync("SELECT DISTINCT \"BirthDate\"::text FROM \"Customers\" WHERE \"BirthDate\" IS NOT NULL"))
            .Should().Equal("1985-03-15");

        // Données HasData : la source fait foi (compteur ORDER porté à 42 côté SQLite).
        (await _db.AdminScalarAsync<long>("SELECT \"CurrentValue\" FROM \"DocumentSequences\" WHERE \"SequenceName\" = 'ORDER'"))
            .Should().Be(42);

        run.Report.GetProperty("SubMicrosecondTruncatedInstants").GetInt64().Should().BeGreaterThan(0);
        run.Report.GetProperty("CivilDates").EnumerateArray().Sum(c => c.GetProperty("TruncatedLegacy").GetInt64()).Should().BeGreaterThan(0);
        run.Report.GetProperty("Tables").EnumerateArray().Should().OnlyContain(t => t.GetProperty("RowsVerified").GetBoolean());
    }

    [PostgreSqlFact]
    public async Task Money_reconciliation_reports_the_source_total_the_imported_total_and_the_rounded_rows()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(8);
        source.Execute("UPDATE \"Sales\" SET \"TotalAmount\" = 2.675 WHERE rowid = (SELECT min(rowid) FROM \"Sales\")");

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        var total = run.Report.GetProperty("NumericColumns").EnumerateArray()
            .Single(c => c.GetProperty("Table").GetString() == "Sales" && c.GetProperty("Column").GetString() == "TotalAmount");
        total.GetProperty("RoundedRows").GetInt64().Should().Be(8);
        total.GetProperty("ServerTotal").GetDecimal().Should().Be(total.GetProperty("ImportedTotal").GetDecimal());
        total.GetProperty("Difference").GetDecimal().Should().Be(-0.005m * 7 + 0.005m,
            "7 × (x.125 → x.12) et 2.675 → 2.68 (au pair, sur la valeur affichée du REAL)");
        (await _db.AdminScalarAsync<decimal>("SELECT \"TotalAmount\" FROM \"Sales\" ORDER BY \"SaleId\" LIMIT 1")).Should().Be(2.68m);
        run.ReportText.Should().Contain("NON présenté comme une restitution fidèle");
    }

    [PostgreSqlFact]
    public async Task Identity_sequences_continue_after_the_largest_imported_identifier()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(5);

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        run.Report.GetProperty("IdentitySequencesResynchronized").GetInt32().Should().BeGreaterThan(10);
        (await _db.AdminScalarAsync<long>("SELECT nextval(pg_get_serial_sequence('\"Suppliers\"', 'SupplierId'))"))
            .Should().Be(SqliteSourceBuilder.IntegerKey(4) + 1, "la prochaine clé suit la plus grande clé importée");
    }

    [PostgreSqlFact]
    public async Task Large_source_is_imported_completely_and_verified_row_by_row()
    {
        const int rows = 2500;
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(rows);

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        (await PgCountAsync("Products")).Should().Be(rows);
        (await PgCountAsync("WorkshopSheetItems")).Should().Be(rows);
        run.Report.GetProperty("Tables").EnumerateArray().Sum(t => t.GetProperty("ImportedRows").GetInt64())
            .Should().Be(20L * rows + 2 + rows);
    }

    // ---- instants : règle ADR-004 décision 8 --------------------------------------------------------------

    [PostgreSqlFact]
    public async Task Ambiguous_local_time_takes_the_standard_offset_and_is_listed_in_the_report()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(2);
        source.Execute("UPDATE \"Users\" SET \"CreatedAt\" = '2026-10-25 02:30:00' WHERE rowid = (SELECT min(rowid) FROM \"Users\")");

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        run.Report.GetProperty("AmbiguousInstantCount").GetInt64().Should().Be(1);
        run.Report.GetProperty("AmbiguousInstants")[0].GetProperty("Detail").GetString().Should().Contain("décalage standard");
        (await _db.AdminScalarAsync<DateTime>("SELECT \"CreatedAt\" FROM \"Users\" ORDER BY \"UserId\" LIMIT 1"))
            .Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
    }

    [PostgreSqlFact]
    public async Task Nonexistent_local_time_rejects_the_source_before_any_server_write()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(2);
        source.Execute("UPDATE \"Users\" SET \"CreatedAt\" = '2026-03-29 02:30:00' WHERE rowid = (SELECT min(rowid) FROM \"Users\")");

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.ImportSourceRejected);
        run.Result.Message.Should().Contain("inexistante");
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync());
    }

    // ---- refus, échecs, retour arrière ---------------------------------------------------------------------

    [PostgreSqlFact]
    public async Task Second_import_is_refused_because_the_target_already_holds_data()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(3);
        (await ImportAsync(source.Path)).Result.ExitCode.Should().Be(MigrationExitCode.Success);
        var after = await PgCountsAsync();

        var again = await ImportAsync(source.Path);

        again.Result.ExitCode.Should().Be(MigrationExitCode.ImportTargetRefused);
        again.Result.Message.Should().Contain("import déjà effectué");
        (await PgCountsAsync()).Should().Equal(after, "rien n'est écrit sur une cible non vierge");
    }

    [PostgreSqlFact]
    public async Task Target_with_a_workstation_write_or_without_migrations_is_refused()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        await LifecycleDatabase.ExecuteAsync(_db.AdminConnectionString,
            "INSERT INTO \"Suppliers\" (\"Name\") VALUES ('Déjà là')");

        var written = await ImportAsync(source.Path);

        written.Result.ExitCode.Should().Be(MigrationExitCode.ImportTargetRefused);
        (await PgCountAsync("Suppliers")).Should().Be(1);

        await using var bare = await LifecycleDatabase.CreateAsync();
        var unmigrated = await ImportAsync(source.Path, target: bare);
        unmigrated.Result.ExitCode.Should().Be(MigrationExitCode.ImportTargetRefused);
        unmigrated.Result.Message.Should().Contain("migrate");
    }

    [PostgreSqlFact]
    public async Task Server_constraint_violation_rolls_back_the_whole_import_and_the_target_is_reread_unchanged()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(4);
        // Doublon que SQLite n'empêche plus (index unique retiré de la copie), refusé par le serveur.
        source.Execute("DROP INDEX \"idx_users_normalized_username_unique\"");
        source.Execute("UPDATE \"Users\" SET \"NormalizedUsername\" = (SELECT min(\"NormalizedUsername\") FROM \"Users\")");

        var run = await ImportAsync(source.Path);

        run.Result.ExitCode.Should().Be(MigrationExitCode.ImportFailed);
        run.Result.Message.Should().Contain("23505").And.Contain("identique à l'état d'avant import");
        run.Report.GetProperty("TargetUnchangedVerified").GetBoolean().Should().BeTrue();
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync(), "aucun import partiel : tout est annulé");
        (await _db.AdminScalarAsync<long>("SELECT \"CurrentValue\" FROM \"DocumentSequences\" WHERE \"SequenceName\" = 'ORDER'"))
            .Should().Be(0, "les données initiales retirées dans la transaction sont restaurées par l'annulation");
    }

    [PostgreSqlFact]
    public async Task Missing_or_altered_row_on_the_server_is_detected_before_commit()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(4);
        await LifecycleDatabase.ExecuteAsync(_db.AdminConnectionString,
            "CREATE FUNCTION it_drop_one() RETURNS trigger LANGUAGE plpgsql AS " +
            $"$$BEGIN IF NEW.\"SupplierId\" = {SqliteSourceBuilder.IntegerKey(1)} THEN RETURN NULL; END IF; RETURN NEW; END$$",
            "CREATE TRIGGER it_drop_one BEFORE INSERT ON \"Suppliers\" FOR EACH ROW EXECUTE FUNCTION it_drop_one()");

        var missing = await ImportAsync(source.Path);

        missing.Result.ExitCode.Should().Be(MigrationExitCode.ImportFailed);
        missing.Result.Message.Should().Contain("Suppliers").And.Contain("ligne");
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync());

        await LifecycleDatabase.ExecuteAsync(_db.AdminConnectionString,
            "DROP TRIGGER it_drop_one ON \"Suppliers\"",
            "CREATE FUNCTION it_alter() RETURNS trigger LANGUAGE plpgsql AS $$BEGIN NEW.\"Name\" := NEW.\"Name\" || '!'; RETURN NEW; END$$",
            "CREATE TRIGGER it_alter BEFORE INSERT ON \"Suppliers\" FOR EACH ROW EXECUTE FUNCTION it_alter()");

        var altered = await ImportAsync(source.Path);

        altered.Result.ExitCode.Should().Be(MigrationExitCode.ImportFailed);
        altered.Result.Message.Should().Contain("colonne Name");
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync());
    }

    [PostgreSqlFact]
    public async Task Interrupted_import_is_rolled_back_by_the_server_and_a_rerun_completes()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(6);
        var tablesDone = 0;

        var interrupted = await ImportAsync(source.Path, afterTable: async (_, _) =>
        {
            if (++tablesDone == 5)
            {
                await LifecycleDatabase.ExecuteAsync(_db.AdminConnectionString,
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity " +
                    $"WHERE datname = current_database() AND usename = '{_db.MigratorRole}'");
            }
        });

        interrupted.Result.ExitCode.Should().Be(MigrationExitCode.ImportFailed);
        interrupted.Report.GetProperty("TargetUnchangedVerified").GetBoolean().Should().BeTrue(interrupted.Result.Message);
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync(), "le serveur annule la transaction d'une session perdue");
        (await _db.AdvisoryHoldersAsync()).Should().Be(0, "le verrou meurt avec sa session");

        var rerun = await ImportAsync(source.Path);

        rerun.Result.ExitCode.Should().Be(MigrationExitCode.Success, rerun.Result.Message);
        (await PgCountAsync("Suppliers")).Should().Be(6);
        (await _db.CompatibilityRowAsync()).Maintenance.Should().BeNull("le marqueur périmé est nettoyé sous verrou puis levé");
    }

    [PostgreSqlFact]
    public async Task Dry_run_imports_and_verifies_everything_then_leaves_the_target_unchanged()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(5);

        var dry = await ImportAsync(source.Path, dryRun: true);

        dry.Result.ExitCode.Should().Be(MigrationExitCode.Success, dry.Result.Message);
        dry.Report.GetProperty("Outcome").GetString().Should().Be("dry-run-rolled-back");
        dry.Report.GetProperty("TargetUnchangedVerified").GetBoolean().Should().BeTrue();
        dry.Report.GetProperty("Tables").EnumerateArray().Should().OnlyContain(t => t.GetProperty("RowsVerified").GetBoolean());
        (await PgCountsAsync()).Should().Equal(await FreshCountsAsync());

        (await ImportAsync(source.Path)).Result.ExitCode.Should().Be(MigrationExitCode.Success, "la cible est restée vierge");
    }

    [PostgreSqlFact]
    public async Task Import_and_migrate_exclude_each_other_through_the_migration_lock()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(3);
        var builder = new NpgsqlConnectionStringBuilder(_db.MigratorConnectionString) { Pooling = false };
        await using (var holder = new NpgsqlConnection(builder.ConnectionString))
        {
            await holder.OpenAsync();
            await using (var command = new NpgsqlCommand($"SELECT pg_advisory_lock({PostgreSqlAdvisoryMigrationLock.LockKey})", holder))
            {
                await command.ExecuteNonQueryAsync();
            }

            var blocked = await ImportAsync(source.Path);

            blocked.Result.ExitCode.Should().Be(MigrationExitCode.LockNotAcquired);
            (await PgCountsAsync()).Should().Equal(await FreshCountsAsync());
        }

        MigrationRunResult? migrateDuringImport = null;
        DateTime? maintenanceDuringImport = null;
        var run = await ImportAsync(source.Path, afterTable: async (table, _) =>
        {
            if (migrateDuringImport is null)
            {
                migrateDuringImport = await _db.RunAsync<ChainS1>(ApplicationVersion.Current);
                maintenanceDuringImport = (await _db.CompatibilityRowAsync()).Maintenance;
            }
        });

        run.Result.ExitCode.Should().Be(MigrationExitCode.Success, run.Result.Message);
        migrateDuringImport!.ExitCode.Should().Be(MigrationExitCode.LockNotAcquired, "aucune migration pendant un import");
        maintenanceDuringImport.Should().NotBeNull("un poste qui démarre pendant l'import est bloqué (maintenance)");
        (await _db.CompatibilityRowAsync()).Maintenance.Should().BeNull();
    }

    [PostgreSqlFact]
    public async Task Unreachable_postgresql_fails_cleanly_and_leaves_the_source_file_untouched()
    {
        using var source = SqliteSourceBuilder.CreateMigrated();
        source.Populate(2);
        var (hashBefore, _) = await SqliteImportSource.HashAsync(source.Path);
        var unreachable = new NpgsqlConnectionStringBuilder(_db.MigratorConnectionString) { Port = 1, Timeout = 3 }.ConnectionString;

        await using var session = PostgreSqlMigrationSession.Create(
            b => DatabaseProviderResolver.Configure(b, new DatabaseProviderOptions { Provider = DatabaseProvider.PostgreSql, ConnectionString = unreachable }),
            MigrationRunner.DefaultLockTimeout);
        var report = Path.Combine(_directory, "unreachable.json");
        var result = await new SqliteImportRunner(session.Ports, session.Connection, ImportPlan.From(session.DesignTimeModel),
                new AcceptingBackupVerification(), new MigrationJournal(), _ => throw new InvalidOperationException("non utilisé"))
            .RunAsync(new SqliteImportRequest(source.Path, Zone, "OP-IT", "b", report, false, TimeSpan.Zero, ApplicationVersion.Current));

        result.ExitCode.Should().Be(MigrationExitCode.ServerUnreachable);
        (await SqliteImportSource.HashAsync(source.Path)).Sha256.Should().Be(hashBefore, "la source est ouverte en lecture seule");
        JsonDocument.Parse(await File.ReadAllTextAsync(report)).RootElement.GetProperty("ExitCode").GetInt32().Should().Be(20);
    }
}
