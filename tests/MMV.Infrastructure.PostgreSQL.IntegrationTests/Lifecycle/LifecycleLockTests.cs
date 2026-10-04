using FluentAssertions;
using MMV.DatabaseManager;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Locking;
using MMV.Infrastructure.Data;
using MMV.Infrastructure.PostgreSQL.IntegrationTests.Infrastructure;
using Npgsql;

namespace MMV.Infrastructure.PostgreSQL.IntegrationTests.Lifecycle;

/// <summary>
/// P4-6B — sérialisation des exécutions de l'outil sur un vrai serveur (S-1, S-2 ; H2, R-3, R-4, R-6).
/// I-1 (deux exécutions simultanées), I-2 (base vide), I-3 (montée à plusieurs migrations), I-4 (plantage).
/// Deux connexions dans un seul processus (Q-13) ; le multi-processus est P4-10.
/// </summary>
public sealed class LifecycleLockTests : LifecycleTestBase
{
    [PostgreSqlFact]
    public async Task I1_two_concurrent_runs_apply_the_DDL_once_and_the_second_exits_12()
    {
        await Db.SetParameterAsync("mmv_it.sleep_seconds", "4");

        var first = Db.RunAsync<ChainS1ThenSlow>("1.0.0");
        await LifecycleDatabase.WaitUntilAsync(async () => await Db.AdvisoryHoldersAsync() == 1,
            TimeSpan.FromSeconds(30), "verrou pris par la première exécution");

        var second = await Db.RunAsync<ChainS1ThenSlow>("1.0.0");
        var firstResult = await first;

        second.ExitCode.Should().Be(MigrationExitCode.LockNotAcquired);
        second.Message.Should().Contain("pid", "le refus nomme l'exécution qui détient le verrou");
        firstResult.ExitCode.Should().Be(MigrationExitCode.Success, firstResult.Message);

        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM mmv_meta.migration_run")).Should().Be(1,
            "la seconde exécution n'a rien écrit, pas même une ligne de journal");
        (await Db.AdminScalarAsync<Guid>("SELECT run_id FROM mmv_meta.migration_run")).Should().Be(firstResult.RunId!.Value);
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM \"__EFMigrationsHistory\"")).Should().Be(2);
    }

    [PostgreSqlFact]
    public async Task I1_two_runs_started_together_end_with_exactly_one_success_and_one_code_12()
    {
        await Db.SetParameterAsync("mmv_it.sleep_seconds", "4");

        var results = await Task.WhenAll(Db.RunAsync<ChainS1ThenSlow>("1.0.0"), Db.RunAsync<ChainS1ThenSlow>("1.0.0"));

        results.Select(r => r.ExitCode).Should().BeEquivalentTo(
            new[] { MigrationExitCode.Success, MigrationExitCode.LockNotAcquired });
        (await Db.AdminScalarAsync<long>("SELECT count(*) FROM mmv_meta.migration_run")).Should().Be(1);
    }

    [PostgreSqlFact]
    public async Task I2_lock_is_taken_on_an_empty_base_before_any_baseline()
    {
        await using var holder = new PostgreSqlAdvisoryMigrationLock(new NpgsqlConnection(Db.MigratorConnectionString));
        await using var contender = new PostgreSqlAdvisoryMigrationLock(new NpgsqlConnection(Db.MigratorConnectionString));

        (await holder.TryAcquireAsync(TimeSpan.Zero)).Should().BeTrue("la clé n'est liée à aucun objet");
        (await Db.AdminScalarAsync<bool>("SELECT to_regclass('\"__EFMigrationsHistory\"') IS NULL")).Should().BeTrue(
            "aucune baseline n'existe : le verrou natif d'EF n'aurait rien à verrouiller");
        (await contender.TryAcquireAsync(TimeSpan.FromMilliseconds(300))).Should().BeFalse();

        await holder.ReleaseAsync();
        (await contender.TryAcquireAsync(TimeSpan.Zero)).Should().BeTrue("la libération explicite rend la clé");
    }

    [PostgreSqlFact]
    public async Task I2_the_tool_holds_the_lock_on_an_empty_base_before_reading_or_writing_anything()
    {
        var observations = new List<(long Holders, bool HistoryExists)>();

        var result = await Db.RunAsync<ChainS1>("1.0.0", decorate: ports => ports with
        {
            Migrator = new ObservingMigrator(ports.Migrator, async () => observations.Add((
                await Db.AdvisoryHoldersAsync(),
                await Db.AdminScalarAsync<bool>("SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL"))))
        });

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        observations.First().Should().Be((1L, false), "étape 3 : verrou tenu, base encore vide");
    }

    [PostgreSqlFact]
    public async Task I3_lock_is_held_across_a_multi_migration_ascent()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);

        var result = await Db.RunAsync<ChainS3>("1.3.0");

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        await using var connection = new NpgsqlConnection(Db.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT step, advisory_holders, xmin::text FROM it_lifecycle_probe ORDER BY step", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var probes = new List<(string Step, long Holders, string Xmin)>();
        while (await reader.ReadAsync())
        {
            probes.Add((reader.GetString(0), reader.GetInt64(1), reader.GetString(2)));
        }

        probes.Select(p => p.Step).Should().Equal("step1", "step2");
        probes.Should().OnlyContain(p => p.Holders == 1, "la session qui migre détient elle-même le verrou pendant chaque migration de la montée");
        probes.Select(p => p.Xmin).Distinct().Should().HaveCount(2,
            "EF applique chaque migration dans sa propre transaction (M-5) : un verrou transactionnel ne couvrirait pas la montée");
    }

    [PostgreSqlFact]
    public async Task Single_session_holds_the_lock_and_runs_the_migration()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.SetParameterAsync("mmv_it.sleep_seconds", "4");

        var running = Db.RunAsync<ChainS2ThenSlow>("1.1.0");
        await WaitForSlowMigrationAsync();

        (await Db.AdminScalarAsync<long>(
            "SELECT count(*) FROM pg_catalog.pg_stat_activity " +
            $"WHERE usename = '{Db.MigratorRole}' AND datname = current_database()"))
            .Should().Be(1, "une seule session PostgreSQL porte toute la séquence");
        (await LockHolderPidAsync()).Should().Be(await SlowMigrationPidAsync(),
            "la session qui exécute Migrate() est celle qui détient le verrou");

        (await running).ExitCode.Should().Be(MigrationExitCode.Success);
    }

    [PostgreSqlFact]
    public async Task Killing_the_lock_holder_stops_the_migration_and_no_concurrent_run_cleans_or_continues_meanwhile()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        await Db.SetParameterAsync("mmv_it.sleep_seconds", "120");

        var first = Db.RunAsync<ChainS2ThenSlow>("1.1.0");
        await WaitForSlowMigrationAsync();
        var marker = (await Db.CompatibilityRowAsync()).Maintenance;
        marker.Should().NotBeNull();

        // Tant que la première migration tourne, une exécution concurrente n'obtient pas le verrou et ne touche
        // pas au marqueur.
        (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.LockNotAcquired);
        (await Db.CompatibilityRowAsync()).Maintenance.Should().Be(marker);

        // Seul le détenteur du verrou est tué : la migration meurt avec lui.
        var holder = await LockHolderPidAsync();
        await Db.AdminScalarAsync<bool>($"SELECT pg_terminate_backend({holder})");
        (await first).ExitCode.Should().Be(MigrationExitCode.MigrationFailed);

        (await Db.AdminScalarAsync<long>(
            "SELECT count(*) FROM pg_catalog.pg_stat_activity " +
            $"WHERE usename = '{Db.MigratorRole}' AND query LIKE '%pg_sleep%' AND state = 'active'"))
            .Should().Be(0, "aucune migration ne continue après la perte du verrou");
        (await Db.AdminScalarAsync<long>(
            $"SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '{ItSlow.Id}'"))
            .Should().Be(0, "la migration interrompue n'est ni validée ni reprise par une session neuve");
        (await Db.CompatibilityRowAsync()).Maintenance.Should().Be(marker,
            "la session perdue n'est jamais rouverte : aucune étape de sortie n'atteint la base");

        // Le détenteur est mort : une nouvelle exécution prend le verrou, puis nettoie le marqueur.
        (await Db.RunAsync<ChainS2>("1.1.0")).ExitCode.Should().Be(MigrationExitCode.Success);
        (await Db.CompatibilityRowAsync()).Maintenance.Should().BeNull();
    }

    private Task WaitForSlowMigrationAsync() => LifecycleDatabase.WaitUntilAsync(async () => await SlowMigrationPidAsync() != 0,
        TimeSpan.FromSeconds(60), "migration lente en cours");

    private async Task<int> SlowMigrationPidAsync() => await Db.AdminScalarAsync<int?>(
        "SELECT pid FROM pg_catalog.pg_stat_activity " +
        $"WHERE usename = '{Db.MigratorRole}' AND state = 'active' AND query LIKE '%pg_sleep%' LIMIT 1") ?? 0;

    private async Task<int> LockHolderPidAsync() => await Db.AdminScalarAsync<int?>(
        "SELECT pid FROM pg_catalog.pg_locks WHERE locktype = 'advisory' AND granted " +
        "AND database = (SELECT oid FROM pg_catalog.pg_database WHERE datname = current_database()) " +
        $"AND classid::bigint = {PostgreSqlAdvisoryMigrationLock.KeyHigh} " +
        $"AND objid::bigint = {PostgreSqlAdvisoryMigrationLock.KeyLow} AND objsubid = 1") ?? 0;

    [PostgreSqlFact]
    public async Task I4_crash_releases_the_lock_leaves_the_last_successful_migration_and_an_open_journal_row()
    {
        (await Db.RunAsync<ChainS1>("1.0.0")).ExitCode.Should().Be(MigrationExitCode.Success);

        var run = await LifecycleCrash.CrashDuringMigrationAsync(Db);

        (await Db.AdvisoryHoldersAsync()).Should().Be(0, "la fin de session libère le verrou");
        await using (var next = new PostgreSqlAdvisoryMigrationLock(new NpgsqlConnection(Db.MigratorConnectionString)))
        {
            (await next.TryAcquireAsync(TimeSpan.Zero)).Should().BeTrue();
        }

        (await Db.AdminScalarAsync<string>(
            "SELECT string_agg(\"MigrationId\", ',' ORDER BY \"MigrationId\") FROM \"__EFMigrationsHistory\""))
            .Should().EndWith(ItStep1.Id, "la base est à la dernière migration réussie, la migration interrompue est annulée");
        (await Db.AdminScalarAsync<string>(
            $"SELECT outcome FROM mmv_meta.migration_run WHERE run_id = '{run}'")).Should().Be("open");
        (await Db.AdminScalarAsync<bool>(
            $"SELECT finished_at IS NULL FROM mmv_meta.migration_run WHERE run_id = '{run}'")).Should().BeTrue();
        var row = await Db.CompatibilityRowAsync();
        (row.Schema, row.Minimum).Should().Be(("1.0.0", "1.0.0"), "la métadonnée n'a pas avancé");
        row.Maintenance.Should().NotBeNull("le marqueur reste posé jusqu'à la relance de l'outil");
    }
}

/// <summary>Décorateur de test : observe l'état serveur au premier appel de lecture de l'historique (étape 3).</summary>
internal sealed class ObservingMigrator(ISchemaMigrator inner, Func<Task> onFirstRead) : ISchemaMigrator
{
    private bool _observed;

    public IReadOnlyList<string> KnownMigrations => inner.KnownMigrations;

    public async Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken cancellationToken = default)
    {
        if (!_observed)
        {
            _observed = true;
            await onFirstRead();
        }

        return await inner.GetAppliedAsync(cancellationToken);
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default) => inner.MigrateAsync(cancellationToken);
}

/// <summary>
/// Plantage du migrateur, émulé côté serveur : pendant une migration lente, toutes les sessions du rôle
/// migrateur sont terminées (verrou, contrôle, migration). Les composants de l'outil ne rouvrent jamais la
/// session de contrôle : comme lors d'une mort du processus, aucune étape de sortie n'atteint la base.
/// </summary>
internal static class LifecycleCrash
{
    public static async Task<Guid> CrashDuringMigrationAsync(LifecycleDatabase db, MigrationJournal? trace = null)
    {
        await db.SetParameterAsync("mmv_it.sleep_seconds", "120");
        var running = db.RunAsync<ChainS2ThenSlow>("1.1.0", trace: trace);

        await LifecycleDatabase.WaitUntilAsync(async () => await db.AdminScalarAsync<long>(
                "SELECT count(*) FROM pg_catalog.pg_stat_activity " +
                $"WHERE usename = '{db.MigratorRole}' AND state = 'active' AND query LIKE '%pg_sleep%'") == 1,
            TimeSpan.FromSeconds(60), "migration lente en cours");

        var runId = await db.AdminScalarAsync<Guid>("SELECT run_id FROM mmv_meta.migration_run WHERE outcome = 'open'");
        await db.AdminScalarAsync<long>(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_catalog.pg_stat_activity " +
            $"WHERE usename = '{db.MigratorRole}'");

        var result = await running;
        result.ExitCode.Should().Be(MigrationExitCode.MigrationFailed, result.Message);
        return runId;
    }
}
