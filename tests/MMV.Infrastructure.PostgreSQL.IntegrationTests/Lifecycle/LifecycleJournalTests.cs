using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Locking;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// P4-6B — journal serveur, maintenance et reprise sur un vrai serveur (S-2 ; H8, DP-6, DP-8, §4.3.1).
/// I-8 (champs, horloge serveur, survie à l'annulation), I-11 (<c>lock_timeout</c>), I-13 (marqueur après
/// plantage), I-14 (base vide, échec initial, relance sans adoption).
/// </summary>
public sealed class LifecycleJournalTests : LifecycleTestBase
{
    [PostgreSqlFact]
    public async Task I8_journal_row_has_every_field_with_server_timestamps()
    {
        var before = await Db.AdminScalarAsync<DateTime>("SELECT clock_timestamp()");
        var result = await Db.RunAsync<ChainS1>("1.0.0");
        var after = await Db.AdminScalarAsync<DateTime>("SELECT clock_timestamp()");
        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);

        await using var connection = new NpgsqlConnection(Db.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT run_id, app_version, applied_before::text, applied_after::text, started_at, finished_at, " +
            "operator_role, operator_reference, run_kind, outcome, failure_cause, backup_reference " +
            "FROM mmv_meta.migration_run", connection);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        reader.GetGuid(0).Should().Be(result.RunId!.Value);
        reader.GetString(1).Should().Be("1.0.0");
        reader.GetString(2).Should().Be("[]");
        reader.GetString(3).Should().Contain("InitialPostgreSqlBaseline");
        var started = reader.GetDateTime(4);
        var finished = reader.GetDateTime(5);
        started.Should().BeOnOrAfter(before).And.BeOnOrBefore(finished);
        finished.Should().BeOnOrBefore(after);
        reader.GetString(6).Should().Be(Db.MigratorRole, "current_user : le rôle serveur, pas l'identité du poste");
        reader.GetString(7).Should().Be("OP-IT");
        reader.GetString(8).Should().Be("migrate");
        reader.GetString(9).Should().Be("success");
        reader.IsDBNull(10).Should().BeTrue();
        reader.GetString(11).Should().Be("dump-it-1.0.0");
        (await reader.ReadAsync()).Should().BeFalse();
    }

    [PostgreSqlFact]
    public async Task I8_failure_trace_survives_the_rolled_back_migration()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);

        var failed = await Db.RunAsync<ChainS2ThenFail>("1.1.0");

        failed.ExitCode.Should().Be(MigrationExitCode.MigrationFailed);
        var id = failed.RunId;
        (await Db.AdminScalarAsync<string>($"SELECT outcome FROM mmv_meta.migration_run WHERE run_id = '{id}'")).Should().Be("failure");
        (await Db.AdminScalarAsync<string>($"SELECT failure_cause FROM mmv_meta.migration_run WHERE run_id = '{id}'")).Should().Contain("22012");
        (await Db.AdminScalarAsync<bool>($"SELECT finished_at IS NOT NULL FROM mmv_meta.migration_run WHERE run_id = '{id}'")).Should().BeTrue();
        (await Db.AdminScalarAsync<string>($"SELECT applied_after::text FROM mmv_meta.migration_run WHERE run_id = '{id}'"))
            .Should().Contain(ItStep1.Id).And.NotContain(ItFail.Id, "Appliquées relues : la migration en échec est annulée");
        var row = await Db.CompatibilityRowAsync();
        (row.Schema, row.Minimum, row.Maintenance).Should().Be(("1.0.0", "1.0.0", (DateTime?)null));
        (await Db.AdvisoryHoldersAsync()).Should().Be(0);
    }

    [PostgreSqlFact]
    public async Task I11_lock_timeout_turns_a_blocked_migration_into_a_clean_failure()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.GrantApplicationDataAccessAsync();

        // Un poste connecté, transaction ouverte sur "Customers" (ACCESS SHARE) : l'ALTER TABLE attend.
        await using var workstation = new NpgsqlConnection(Db.AppConnectionString);
        await workstation.OpenAsync();
        await using var transaction = await workstation.BeginTransactionAsync();
        await using (var read = new NpgsqlCommand("SELECT count(*) FROM \"Customers\"", workstation, transaction))
        {
            await read.ExecuteScalarAsync();
        }

        var clock = Stopwatch.StartNew();
        var blocked = await Db.RunAsync<ChainS1ThenAlter>("1.1.0", lockTimeout: TimeSpan.FromSeconds(1));
        clock.Stop();

        blocked.ExitCode.Should().Be(MigrationExitCode.MigrationFailed, blocked.Message);
        blocked.Message.Should().Contain("55P03", "lock_not_available : échec propre, pas d'attente indéfinie");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        (await Db.AdminScalarAsync<string>(
            $"SELECT outcome FROM mmv_meta.migration_run WHERE run_id = '{blocked.RunId}'")).Should().Be("failure");
        var row = await Db.CompatibilityRowAsync();
        (row.Schema, row.Minimum, row.Maintenance).Should().Be(("1.0.0", "1.0.0", (DateTime?)null));
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM \"__EFMigrationsHistory\"")).Should().Be(1);
        (await Db.AdvisoryHoldersAsync()).Should().Be(0);

        await transaction.CommitAsync();
        (await Db.RunAsync<ChainS1ThenAlter>("1.1.0", lockTimeout: TimeSpan.FromSeconds(1))).ExitCode
            .Should().Be(MigrationExitCode.Success, "poste fermé : la même migration passe");
    }

    [PostgreSqlFact]
    public async Task I13_marker_left_by_a_killed_run_blocks_whatever_its_age_and_is_cleaned_only_under_lock()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.GrantApplicationDataAccessAsync();
        await LifecycleCrash.CrashDuringMigrationAsync(Db);

        await LifecycleDatabase.ExecuteAsync(Db.AdminConnectionString,
            "UPDATE mmv_meta.schema_compatibility SET maintenance_started_at = '2000-01-01T00:00:00Z'");
        await using (var workstation = Db.Context<ChainS2>(Db.AppConnectionString))
        {
            var verdict = await ServerSchemaCompatibilityGuard.EvaluateAsync(
                workstation.Database.GetDbConnection(), workstation.Database.GetMigrations(), "1.1.0");
            verdict.State.Should().Be(ServerSchemaState.E5, "un marqueur ancien bloque comme un récent");
            verdict.CanStart.Should().BeFalse();
        }

        // Une seconde exécution SANS verrou n'y touche pas.
        await using (var holder = new PostgreSqlAdvisoryMigrationLock(new NpgsqlConnection(Db.MigratorConnectionString)))
        {
            (await holder.TryAcquireAsync(TimeSpan.Zero)).Should().BeTrue();
            (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.LockNotAcquired);
            (await Db.CompatibilityRowAsync()).Maintenance.Should().Be(
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), "nettoyer avant le verrou est INTERDIT");
        }

        // AVEC le verrou, elle le nettoie et le trace.
        var trace = new MigrationJournal();
        var resumed = await Db.RunAsync<ChainS2>("1.1.0", trace: trace);

        resumed.ExitCode.Should().Be(MigrationExitCode.Success, resumed.Message);
        (await Db.CompatibilityRowAsync()).Maintenance.Should().BeNull();
        trace.Entries.Should().Contain(e => e.Contains("périmé") && e.Contains("2000-01-01"));
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM mmv_meta.migration_run WHERE outcome = 'open'"))
            .Should().Be(1, "la ligne 'open' de l'exécution tuée reste la preuve de son échec");
    }

    /// <summary>
    /// P4-8 : le point d'entrée réel impose TLS VerifyFull et SCRAM-SHA-256 à la chaîne du migrateur (D-14) ; ce test
    /// s'exécute donc sur le serveur TLS de test. Assertions inchangées depuis P4-6B.
    /// </summary>
    [PostgreSqlTlsFact]
    public async Task Status_through_the_real_entry_point_reads_only_and_reports_the_server_age_of_the_marker()
    {
        await using var Db = await LifecycleDatabase.CreateAsync(
            PostgreSqlTestEnvironment.GetRequired(PostgreSqlTestEnvironment.TlsConnectionStringVariableName));
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await LifecycleDatabase.ExecuteAsync(Db.AdminConnectionString,
            "UPDATE mmv_meta.schema_compatibility SET maintenance_started_at = now() - interval '3 days'");
        var journalBefore = await Db.AdminScalarAsync<long>("SELECT count(*) FROM mmv_meta.migration_run");
        var output = new StringWriter();
        var trace = Path.Combine(Path.GetTempPath(), $"mmv-it-status-{Guid.NewGuid():N}.log");

        int code;
        try
        {
            code = await Program.RunAsync(["status"],
                new Dictionary<string, string?> { ["MMV_MIGRATOR_CONNECTION_STRING"] = Db.MigratorConnectionString },
                output, new StringWriter(), trace);
        }
        finally
        {
            File.Delete(trace);
        }

        code.Should().Be(0);
        output.ToString().Should().Contain("âge serveur 3.00:00:00").And.Contain("E5").And.Contain("InitialPostgreSqlBaseline");
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM mmv_meta.migration_run")).Should().Be(journalBefore);
        (await Db.CompatibilityRowAsync()).Maintenance.Should().NotBeNull("status ne lève jamais la maintenance");
    }

    /// <summary>
    /// P4-8 (D-14) : sur le serveur de test SANS TLS, le même point d'entrée échoue (code 20) au lieu de se
    /// rabattre sur une connexion non chiffrée — l'absence de repli est prouvée par le point d'entrée réel.
    /// </summary>
    [PostgreSqlFact]
    public async Task Status_through_the_real_entry_point_never_falls_back_to_an_unencrypted_connection()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        var error = new StringWriter();

        var code = await Program.RunAsync(["status"],
            new Dictionary<string, string?> { ["MMV_MIGRATOR_CONNECTION_STRING"] = Db.MigratorConnectionString },
            new StringWriter(), error, Path.Combine(Path.GetTempPath(), $"mmv-it-{Guid.NewGuid():N}.log"));

        code.Should().Be(20);
        error.ToString().Should().NotContain("mmv_it_ephemeral");
    }

    [PostgreSqlFact]
    public async Task Migrate_through_the_real_entry_point_is_refused_with_code_11_and_writes_nothing()
    {
        var code = await Program.RunAsync(["migrate", "--operator", "OP-IT", "--backup-ref", "dump", "--app-role", Db.AppRole],
            new Dictionary<string, string?> { ["MMV_MIGRATOR_CONNECTION_STRING"] = Db.MigratorConnectionString },
            new StringWriter(), new StringWriter(), Path.Combine(Path.GetTempPath(), $"mmv-it-{Guid.NewGuid():N}.log"));

        code.Should().Be(11, "« dump » n'est pas le chemin d'un manifeste vérifié (P4-9) : refus avant tout contact serveur");
        (await Db.AdminScalarAsync<bool>("SELECT to_regnamespace('mmv_meta') IS NULL")).Should().BeTrue();
        (await Db.AdminScalarAsync<bool>("SELECT to_regclass('\"__EFMigrationsHistory\"') IS NULL")).Should().BeTrue();
    }

    [PostgreSqlFact]
    public async Task I14_variant_first_install_failing_at_step_9_then_relaunch_anchors_the_verified_baseline()
    {
        // Scénario 1 : baseline physiquement appliquée, vérification (GRANT) en échec, puis relance.
        await Db.SetParameterAsync("mmv_it.app_role", Db.AppRole);

        var failed = await Db.RunAsync<ChainS1ThenRevoke>("1.0.0");
        failed.ExitCode.Should().Be(MigrationExitCode.VerificationFailed, failed.Message);
        var blocked = await Db.CompatibilityRowAsync();
        (blocked.Schema, blocked.Minimum).Should().Be(((string?)null, (string?)null));
        blocked.Maintenance.Should().NotBeNull("état non vérifié : jamais une ancre");

        var relaunched = await Db.RunAsync<ChainS1ThenRevoke>("1.0.0");

        relaunched.ExitCode.Should().Be(MigrationExitCode.Success, relaunched.Message);
        (await Db.CompatibilityRowAsync()).Should().Be(("1.0.0", "1.0.0", (DateTime?)null),
            "l'état physique est enfin vérifié : la base sort de l'initialisation sans adoption");
    }

    [PostgreSqlFact]
    public async Task I14_empty_base_initial_failure_then_resume_without_adoption()
    {
        await LifecycleDatabase.ExecuteAsync(Db.MigratorConnectionString,
            "CREATE TABLE \"Customers\" (it_homonym integer)");

        var failed = await Db.RunAsync<ChainS1>("1.0.0");

        failed.ExitCode.Should().Be(MigrationExitCode.MigrationFailed, failed.Message);
        failed.Message.Should().Contain("42P07");
        var row = await Db.CompatibilityRowAsync();
        row.Schema.Should().BeNull();
        row.Minimum.Should().BeNull();
        row.Maintenance.Should().NotBeNull("l'étape 11 ne nettoie pas une ligne non initialisée");
        (await Db.AdminScalarAsync<string>(
            $"SELECT outcome FROM mmv_meta.migration_run WHERE run_id = '{failed.RunId}'")).Should().Be("failure");

        await using (var observer = Db.Context<ChainS1>(Db.MigratorConnectionString))
        {
            var verdict = await ServerSchemaCompatibilityGuard.EvaluateAsync(
                observer.Database.GetDbConnection(), observer.Database.GetMigrations(), "1.0.0");
            verdict.State.Should().Be(ServerSchemaState.E5);
            verdict.InitializationIncomplete.Should().BeTrue("installation inachevée, jamais E1");
        }

        await LifecycleDatabase.ExecuteAsync(Db.MigratorConnectionString, "DROP TABLE \"Customers\"");
        var resumed = await Db.RunAsync<ChainS1>("1.0.0");

        resumed.ExitCode.Should().Be(MigrationExitCode.Success, resumed.Message);
        (await Db.CompatibilityRowAsync()).Should().Be(("1.0.0", "1.0.0", (DateTime?)null));
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM mmv_meta.migration_run WHERE run_kind = 'adopt'")).Should().Be(0);
    }
}
