using FluentAssertions;
using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Tests.TestDoubles;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Tests;

/// <summary>
/// P4-6B — séquence de <see cref="MigrationRunner"/> (§2.3 du plan) sur doubles enregistreurs, sans serveur.
/// U-C1 (opérateur), U-C2 (sauvegarde), U-C7 (rôle applicatif), U-C8 (ordre), Q-22 (métadonnée absente).
/// </summary>
public sealed class MigrationRunnerTests
{
    private static readonly DateTimeOffset AncientMarker = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static MigrationRunRequest Request(
        MigrationRunKind kind = MigrationRunKind.Migrate,
        string @operator = "OP-42",
        string? backupReference = "backup-2026-10-04",
        string appRole = "mmv_app",
        string version = "1.1.0") =>
        new(kind, @operator, backupReference, appRole, TimeSpan.Zero, version);

    private static (MigrationRunner Runner, MigrationJournal Trace) Runner(RecordingPorts ports, IBackupVerification? backup = null)
    {
        var trace = new MigrationJournal();
        return (new MigrationRunner(ports.Ports, backup ?? ports.Backup, trace), trace);
    }

    private static RecordingPorts Installed() => new()
    {
        Applied = ["B"],
        Known = ["B", "M1"],
        Metadata = ServerCompatibilityMetadata.FromRow("1.0.0", "1.0.0", null),
        PreviousRuns = { new MigrationRunRecord("1.0.0", MigrationRunKind.Migrate, MigrationOutcome.Success, [], ["B"]) }
    };

    private static void ShouldNotWriteAnything(RecordingPorts ports) =>
        ports.Calls.Should().NotContain(RecordingPorts.WriteCalls);

    private static void ShouldBeInOrder(RecordingPorts ports, params string[] calls)
    {
        var indexes = calls.Select(c => ports.Calls.IndexOf(c)).ToArray();
        indexes.Should().NotContain(-1, $"chaque appel attendu doit avoir eu lieu ; reçus : {string.Join(", ", ports.Calls)}");
        indexes.Should().BeInAscendingOrder($"ordre attendu : {string.Join(" → ", calls)} ; reçus : {string.Join(", ", ports.Calls)}");
    }

    // ---- étape 0 : options (U-C1, U-C7) ----------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_operator_is_code_10_without_any_server_contact(string @operator)
    {
        var ports = Installed();

        var result = await Runner(ports).Runner.RunAsync(Request(@operator: @operator));

        result.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        ports.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MigrationRunKind.Migrate)]
    [InlineData(MigrationRunKind.Adopt)]
    public async Task Missing_app_role_is_code_10_without_any_server_contact(MigrationRunKind kind)
    {
        var ports = Installed();

        var result = await Runner(ports).Runner.RunAsync(Request(kind, appRole: " "));

        result.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        ports.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_app_role_is_code_10_after_the_lock_and_before_any_write()
    {
        var ports = Installed();
        ports.RoleExists = false;

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        ShouldBeInOrder(ports, "lock.acquire", "grants.exists", "lock.release");
        ShouldNotWriteAnything(ports);
    }

    // ---- étape 1 : sauvegarde (U-C2) -------------------------------------------------------------------

    [Fact]
    public async Task Refused_backup_is_code_11_before_the_lock_and_without_any_write()
    {
        var ports = Installed();

        var result = await Runner(ports, new ProofBackupVerification(BackupPolicy.MaximumAgeBeforeMigration))
            .Runner.RunAsync(Request(backupReference: Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.manifest.json")));

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified);
        ports.Calls.Should().BeEmpty("le refus précède le verrou : aucune entrée en base, aucun verrou pris");
    }

    // ---- étape 3a : sauvegarde ⇔ base courante (P4-9) ----------------------------------------------------

    [Fact]
    public async Task Backup_not_matching_the_current_database_is_code_11_under_the_lock_and_before_any_write()
    {
        var ports = Installed();
        ports.BackupMismatch = "la base a changé depuis la sauvegarde";

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified);
        result.Message.Should().Contain("la base a changé depuis la sauvegarde");
        ports.Calls.Should().Equal("backup.verify", "lock.acquire", "state.read", "backup.confirm", "lock.release");
        ShouldNotWriteAnything(ports);
    }

    [Fact]
    public async Task Backup_is_confirmed_against_the_current_database_first_under_the_lock()
    {
        var ports = Installed();

        (await Runner(ports).Runner.RunAsync(Request())).ExitCode.Should().Be(MigrationExitCode.Success);

        ShouldBeInOrder(ports, "backup.verify", "lock.acquire", "state.read", "backup.confirm", "migrator.applied",
            "metadata.ensure", "migrator.migrate");
        ports.Calls.IndexOf("backup.confirm").Should().Be(ports.Calls.IndexOf("lock.acquire") + 2,
            "le rapprochement est le premier contrôle sous verrou");
    }

    [Fact]
    public async Task Missing_backup_reference_is_code_11_without_calling_the_verification()
    {
        var ports = Installed();

        var result = await Runner(ports).Runner.RunAsync(Request(backupReference: null));

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified);
        ports.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Refused_backup_from_the_port_is_code_11()
    {
        var ports = Installed();
        ports.BackupVerified = false;

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.BackupNotVerified);
        ports.Calls.Should().Equal("backup.verify");
    }

    // ---- étape 2 : verrou ------------------------------------------------------------------------------

    [Fact]
    public async Task Lock_not_acquired_is_code_12_without_any_write_and_without_touching_the_marker()
    {
        var ports = Installed();
        ports.LockAvailable = false;
        ports.StaleMarker = AncientMarker;

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.LockNotAcquired);
        result.Message.Should().Contain("pid 4242", "le message nomme l'autre exécution");
        ports.Calls.Should().Equal("backup.verify", "lock.acquire");
        ports.StaleMarker.Should().Be(AncientMarker, "nettoyer sans verrou est INTERDIT");
    }

    // ---- U-C8 : ordre de la séquence -------------------------------------------------------------------

    [Fact]
    public async Task Success_follows_the_normative_order()
    {
        var ports = Installed();

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.Success);
        ShouldBeInOrder(ports,
            "backup.verify", "lock.acquire", "grants.exists", "metadata.ensure", "grants.grant",
            "metadata.clear-stale", "metadata.begin", "journal.open", "migrator.migrate", "verify",
            "metadata.advance", "journal.close", "metadata.end", "lock.release");
        ports.Closed!.Value.Outcome.Should().Be(MigrationOutcome.Success);
        ports.GrantedRole.Should().Be("mmv_app");
    }

    [Fact]
    public async Task No_write_happens_before_the_lock()
    {
        var ports = Installed();

        await Runner(ports).Runner.RunAsync(Request());

        var lockIndex = ports.Calls.IndexOf("lock.acquire");
        ports.Calls.Take(lockIndex).Should().NotContain(RecordingPorts.WriteCalls);
    }

    [Fact]
    public async Task Stale_marker_is_cleaned_only_after_the_lock_and_the_cleanup_is_traced()
    {
        var ports = Installed();
        ports.StaleMarker = AncientMarker;
        var (runner, trace) = Runner(ports);

        await runner.RunAsync(Request());

        ShouldBeInOrder(ports, "lock.acquire", "metadata.clear-stale", "metadata.begin");
        trace.Entries.Should().Contain(e => e.Contains("périmé") && e.Contains("2001-01-01"));
    }

    [Fact]
    public async Task Verification_failure_is_code_14_and_the_metadata_does_not_advance()
    {
        var ports = Installed();
        ports.VerificationFailure = "droits du rôle applicatif non assurés";

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.VerificationFailed);
        ports.Calls.Should().NotContain("metadata.advance");
        ports.Closed!.Value.Outcome.Should().Be(MigrationOutcome.Failure);
        ports.Closed!.Value.Cause.Should().Contain("droits du rôle applicatif");
        ShouldBeInOrder(ports, "verify", "journal.close", "metadata.end", "lock.release");
    }

    [Fact]
    public async Task Migration_failure_is_code_13_and_exits_10b_then_11_then_12()
    {
        var ports = Installed();
        ports.Failing.Add("migrator.migrate");

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.MigrationFailed);
        ports.Calls.Should().NotContain(["verify", "metadata.advance"]);
        ports.Closed!.Value.Outcome.Should().Be(MigrationOutcome.Failure);
        ports.Closed!.Value.Cause.Should().Contain("échec programmé");
        ShouldBeInOrder(ports, "migrator.migrate", "journal.close", "metadata.end", "lock.release");
    }

    [Fact]
    public async Task Metadata_advance_failure_is_code_14_and_never_success()
    {
        var ports = Installed();
        ports.Failing.Add("metadata.advance");

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.VerificationFailed);
        ports.Closed!.Value.Outcome.Should().Be(MigrationOutcome.Failure);
        ShouldBeInOrder(ports, "metadata.advance", "journal.close", "metadata.end", "lock.release");
    }

    [Fact]
    public async Task Ddl_failure_is_code_13_releases_the_lock_and_writes_no_journal()
    {
        var ports = Installed();
        ports.Failing.Add("metadata.ensure");

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.MigrationFailed);
        ports.Calls.Should().NotContain(["journal.open", "metadata.begin", "metadata.end"]);
        ports.Calls.Last().Should().Be("lock.release");
    }

    [Fact]
    public async Task Journal_open_failure_still_clears_maintenance_then_releases_the_lock()
    {
        var ports = Installed();
        ports.Failing.Add("journal.open");

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.MigrationFailed);
        ports.Calls.Should().NotContain(["migrator.migrate", "journal.close"]);
        ShouldBeInOrder(ports, "metadata.begin", "journal.open", "metadata.end", "lock.release");
    }

    [Fact]
    public async Task Lock_is_released_even_when_maintenance_cleanup_fails()
    {
        var ports = Installed();
        ports.Failing.Add("metadata.end");

        await Runner(ports).Runner.RunAsync(Request());

        ports.Calls.Last().Should().Be("lock.release");
    }

    [Fact]
    public async Task Local_trace_opens_before_and_closes_after_everything()
    {
        var ports = Installed();
        var (runner, trace) = Runner(ports);

        var result = await runner.RunAsync(Request());

        trace.Entries.First().Should().Contain("OPEN").And.Contain(result.RunId.ToString()!);
        trace.Entries.Last().Should().Contain("CLOSE").And.Contain("code 0");
    }

    // ---- journal ouvert : champs ---------------------------------------------------------------------

    [Fact]
    public async Task Journal_open_carries_operator_backup_version_and_applied_before()
    {
        var ports = Installed();

        var result = await Runner(ports).Runner.RunAsync(Request());

        var run = ports.OpenedRun!;
        run.RunId.Should().Be(result.RunId!.Value);
        run.OperatorReference.Should().Be("OP-42");
        run.BackupReference.Should().Be("backup-2026-10-04");
        run.ApplicationVersion.Should().Be("1.1.0");
        run.Kind.Should().Be(MigrationRunKind.Migrate);
        run.AppliedBefore.Should().Equal("B");
        ports.Closed!.Value.AppliedAfter.Should().Equal("B", "M1");
    }

    [Fact]
    public async Task Advance_receives_previous_successful_runs_plus_the_current_run()
    {
        var ports = Installed();

        await Runner(ports).Runner.RunAsync(Request());

        ports.AdvancedWith.Should().HaveCount(2);
        ports.AdvancedWith![^1].Should().BeEquivalentTo(new MigrationRunRecord(
            "1.1.0", MigrationRunKind.Migrate, MigrationOutcome.Success, ["B"], ["B", "M1"]));
    }

    // ---- étape 3 : Q-22 ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(NoMetadata))]
    public async Task Migrated_base_without_metadata_is_code_15_and_requires_adoption(ServerCompatibilityMetadata metadata)
    {
        var ports = Installed();
        ports.Metadata = metadata;

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.MetadataInconsistent);
        result.Message.Should().Contain("adopt-compatibility");
        ShouldNotWriteAnything(ports);
        ports.Calls.Last().Should().Be("lock.release");
    }

    public static TheoryData<ServerCompatibilityMetadata> NoMetadata() => new()
    {
        ServerCompatibilityMetadata.Absent,
        ServerCompatibilityMetadata.NoRow
    };

    [Fact]
    public async Task Unreadable_metadata_is_code_15()
    {
        var ports = Installed();
        ports.Metadata = ServerCompatibilityMetadata.FromRow("garbage", "1.0.0", null);

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.MetadataInconsistent);
        ShouldNotWriteAnything(ports);
    }

    [Theory]
    [MemberData(nameof(EmptyBaseMetadata))]
    public async Task Empty_base_is_an_installation(ServerCompatibilityMetadata metadata)
    {
        var ports = new RecordingPorts { Applied = [], Known = ["B"], Metadata = metadata };

        var result = await Runner(ports).Runner.RunAsync(Request(version: "1.0.0"));

        result.ExitCode.Should().Be(MigrationExitCode.Success);
        ports.Calls.Should().Contain("migrator.migrate");
    }

    public static TheoryData<ServerCompatibilityMetadata> EmptyBaseMetadata() => new()
    {
        ServerCompatibilityMetadata.Absent,
        ServerCompatibilityMetadata.NoRow,
        ServerCompatibilityMetadata.FromRow(null, null, AncientMarker)
    };

    [Fact]
    public async Task Initialization_row_on_a_partially_migrated_base_resumes_without_adoption()
    {
        var ports = new RecordingPorts
        {
            Applied = ["B"],
            Known = ["B", "M1"],
            Metadata = ServerCompatibilityMetadata.FromRow(null, null, AncientMarker),
            StaleMarker = AncientMarker,
            PreviousRuns = { new MigrationRunRecord("1.0.0", MigrationRunKind.Migrate, MigrationOutcome.Failure, [], ["B"]) }
        };

        var result = await Runner(ports).Runner.RunAsync(Request());

        result.ExitCode.Should().Be(MigrationExitCode.Success);
    }

    [Fact]
    public async Task Relaunch_after_a_first_install_failing_at_step_9_anchors_the_verified_baseline()
    {
        var ports = new RecordingPorts
        {
            Applied = ["B"],
            Known = ["B"],
            Metadata = ServerCompatibilityMetadata.FromRow(null, null, AncientMarker),
            PreviousRuns = { new MigrationRunRecord("1.0.0", MigrationRunKind.Migrate, MigrationOutcome.Failure, [], ["B"]) }
        };

        var result = await Runner(ports).Runner.RunAsync(Request(version: "1.0.0"));

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        CompatibilityMetadataWriter.Compute(ports.AdvancedWith!)
            .Should().BeEquivalentTo(new { SchemaVersion = "1.0.0", MinimumSupportedVersion = "1.0.0" });
    }

    [Fact]
    public async Task Run_that_leaves_the_row_in_initialization_state_is_never_a_success()
    {
        // État incohérent : un succès antérieur existe, mais la ligne a été remise en initialisation. Aucune
        // évolution n'est calculée ; la ligne resterait en initialisation : ce n'est jamais un succès (§4.3.1).
        var ports = new RecordingPorts
        {
            Applied = ["B"],
            Known = ["B"],
            Metadata = ServerCompatibilityMetadata.FromRow(null, null, AncientMarker),
            PreviousRuns = { new MigrationRunRecord("1.0.0", MigrationRunKind.Migrate, MigrationOutcome.Success, [], ["B"]) }
        };

        var result = await Runner(ports).Runner.RunAsync(Request(version: "1.0.0"));

        result.ExitCode.Should().Be(MigrationExitCode.VerificationFailed);
        result.Message.Should().Contain("initialisation");
        ports.Closed!.Value.Outcome.Should().Be(MigrationOutcome.Failure);
    }

    [Fact]
    public async Task Cancellation_during_migration_still_closes_the_journal_row()
    {
        using var cancellation = new CancellationTokenSource();
        var ports = Installed();
        ports.OnMigrate = cancellation.Cancel;
        ports.Failing.Add("migrator.migrate");

        var result = await new MigrationRunner(ports.Ports, ports.Backup, new MigrationJournal())
            .RunAsync(Request(), cancellation.Token);

        result.ExitCode.Should().Be(MigrationExitCode.MigrationFailed);
        ports.Closed.Should().NotBeNull("le processus est vivant : la ligne ne reste pas 'open'");
        ports.Closed!.Value.Outcome.Should().Be(MigrationOutcome.Failure);
    }

    // ---- adopt-compatibility ---------------------------------------------------------------------------

    [Fact]
    public async Task Adopt_follows_the_sequence_without_migrating()
    {
        var ports = new RecordingPorts { Applied = ["B"], Known = ["B"], Metadata = ServerCompatibilityMetadata.Absent };

        var result = await Runner(ports).Runner.RunAsync(Request(MigrationRunKind.Adopt, version: "1.0.0"));

        result.ExitCode.Should().Be(MigrationExitCode.Success);
        ports.Calls.Should().NotContain("migrator.migrate");
        ShouldBeInOrder(ports, "backup.verify", "lock.acquire", "metadata.ensure", "grants.grant",
            "metadata.begin", "journal.open", "verify", "metadata.advance", "journal.close", "metadata.end",
            "lock.release");
        ports.OpenedRun!.Kind.Should().Be(MigrationRunKind.Adopt);
    }

    [Fact]
    public async Task Adopt_refuses_an_already_initialized_metadata()
    {
        var ports = Installed();

        var result = await Runner(ports).Runner.RunAsync(Request(MigrationRunKind.Adopt));

        result.ExitCode.Should().Be(MigrationExitCode.MetadataInconsistent);
        ShouldNotWriteAnything(ports);
    }

    [Fact]
    public async Task Adopt_refuses_an_empty_base()
    {
        var ports = new RecordingPorts { Applied = [], Metadata = ServerCompatibilityMetadata.Absent };

        var result = await Runner(ports).Runner.RunAsync(Request(MigrationRunKind.Adopt));

        result.ExitCode.Should().Be(MigrationExitCode.MetadataInconsistent);
        ShouldNotWriteAnything(ports);
    }

    // ---- status ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_reads_only_and_takes_no_lock()
    {
        var ports = Installed();
        ports.Metadata = ServerCompatibilityMetadata.FromRow("1.0.0", "1.0.0", AncientMarker);
        var output = new StringWriter();

        var code = await Runner(ports).Runner.StatusAsync("1.1.0", output);

        code.Should().Be(MigrationExitCode.Success);
        ports.Calls.Should().NotContain(RecordingPorts.WriteCalls).And.NotContain("lock.acquire");
        var text = output.ToString();
        text.Should().Contain("1.1.0").And.Contain("2001-01-01").And.Contain("M1");
        text.Should().Contain("âge serveur 2.03:00:00", "l'âge vient de l'horloge du serveur, jamais du poste (ADR-004, T9)");
    }
}
