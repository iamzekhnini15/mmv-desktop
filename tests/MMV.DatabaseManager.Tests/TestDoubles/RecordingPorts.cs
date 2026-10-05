using MMV.DatabaseManager.Backup;
using MMV.DatabaseManager.Journal;
using MMV.DatabaseManager.Locking;
using MMV.DatabaseManager.Permissions;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Tests.TestDoubles;

/// <summary>
/// Doubles enregistreurs de tous les ports de <see cref="MigrationRunner"/> (U-C8) : chaque appel est inscrit,
/// dans l'ordre, dans un journal partagé ; un appel peut être programmé pour échouer.
/// </summary>
public sealed class RecordingPorts
{
    /// <summary>Appels qui écrivent en base (DDL, GRANT, lignes de métadonnée ou de journal, migration).</summary>
    public static readonly string[] WriteCalls =
    [
        "metadata.ensure", "grants.grant", "metadata.clear-stale", "metadata.begin", "journal.open",
        "migrator.migrate", "metadata.advance", "journal.close", "metadata.end"
    ];

    public List<string> Calls { get; } = new();

    /// <summary>Appels programmés pour lever une exception.</summary>
    public HashSet<string> Failing { get; } = new();

    public bool BackupVerified { get; set; } = true;

    /// <summary>Refus programmé du rapprochement sauvegarde ⇔ base courante (P4-9).</summary>
    public string? BackupMismatch { get; set; }
    public bool LockAvailable { get; set; } = true;
    public bool RoleExists { get; set; } = true;
    public string? VerificationFailure { get; set; }
    public ServerCompatibilityMetadata Metadata { get; set; } = ServerCompatibilityMetadata.NoRow;
    public DateTimeOffset? StaleMarker { get; set; }
    public List<string> Applied { get; set; } = new();
    public List<string> Known { get; set; } = ["B"];
    public List<MigrationRunRecord> PreviousRuns { get; } = new();

    /// <summary>Action exécutée au début de la migration (ex. annulation du jeton par l'appelant).</summary>
    public Action? OnMigrate { get; set; }

    /// <summary>Paramètres reçus par les appels qui en portent.</summary>
    public ServerMigrationRun? OpenedRun { get; private set; }
    public (MigrationOutcome Outcome, string? Cause, IReadOnlyList<string> AppliedAfter)? Closed { get; private set; }
    public IReadOnlyList<MigrationRunRecord>? AdvancedWith { get; private set; }
    public string? GrantedRole { get; private set; }

    public IBackupVerification Backup => new BackupDouble(this);

    public MigrationPorts Ports => new(
        new LockDouble(this), new MigratorDouble(this), new MetadataDouble(this),
        new JournalDouble(this), new GrantsDouble(this), new VerificationDouble(this), new FingerprintDouble(this));

    private void Record(string call)
    {
        Calls.Add(call);
        if (Failing.Contains(call))
        {
            throw new InvalidOperationException($"échec programmé : {call}");
        }
    }

    private sealed class BackupDouble(RecordingPorts p) : IBackupVerification
    {
        public Task<BackupVerificationResult> VerifyAsync(string backupReference, CancellationToken cancellationToken)
        {
            p.Record("backup.verify");
            return Task.FromResult(p.BackupVerified
                ? BackupVerificationResult.Verified()
                : BackupVerificationResult.Refused("refus programmé"));
        }

        public BackupVerificationResult ConfirmCurrent(BackupVerificationResult verified, DatabaseFingerprint live)
        {
            p.Record("backup.confirm");
            return p.BackupMismatch is null ? verified : BackupVerificationResult.Refused(p.BackupMismatch);
        }
    }

    private sealed class FingerprintDouble(RecordingPorts p) : IDatabaseFingerprintReader
    {
        public Task<DatabaseFingerprint> ReadAsync(CancellationToken cancellationToken)
        {
            p.Record("state.read");
            return Task.FromResult(new DatabaseFingerprint("7000000000000000001", "mmv", 16384,
                DateTimeOffset.UnixEpoch, p.Applied.ToArray(), []));
        }
    }

    private sealed class LockDouble(RecordingPorts p) : IMigrationLock
    {
        public Task<bool> TryAcquireAsync(TimeSpan wait, CancellationToken cancellationToken)
        {
            p.Record("lock.acquire");
            return Task.FromResult(p.LockAvailable);
        }

        public Task<string?> DescribeHolderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>("pid 4242");

        public Task ReleaseAsync(CancellationToken cancellationToken)
        {
            p.Record("lock.release");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MigratorDouble(RecordingPorts p) : ISchemaMigrator
    {
        public IReadOnlyList<string> KnownMigrations => p.Known;

        public Task<IReadOnlyList<string>> GetAppliedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            p.Record("migrator.applied");
            return Task.FromResult<IReadOnlyList<string>>(p.Applied.ToArray());
        }

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            p.OnMigrate?.Invoke();
            p.Record("migrator.migrate");
            p.Applied = p.Known.ToList();
            return Task.CompletedTask;
        }
    }

    private sealed class MetadataDouble(RecordingPorts p) : ICompatibilityMetadataWriter
    {
        public Task<ServerCompatibilityMetadata> ReadAsync(CancellationToken cancellationToken)
        {
            p.Record("metadata.read");
            return Task.FromResult(p.Metadata);
        }

        public Task<ServerSchemaVerdict> EvaluateAsync(
            IEnumerable<string> known, string applicationVersion, CancellationToken cancellationToken)
        {
            p.Record("metadata.evaluate");
            return Task.FromResult(new ServerSchemaVerdict(
                ServerSchemaState.E1, CompatibilityCase.C1, true, false, "Base compatible."));
        }

        public Task<TimeSpan?> MaintenanceAgeAsync(CancellationToken cancellationToken)
        {
            p.Record("metadata.age");
            return Task.FromResult<TimeSpan?>(p.Metadata.MaintenanceStartedAt is null ? null : TimeSpan.FromHours(51));
        }

        public Task EnsureSchemaAsync(CancellationToken cancellationToken)
        {
            p.Record("metadata.ensure");
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> ClearStaleMaintenanceAsync(Guid runId, CancellationToken cancellationToken)
        {
            p.Record("metadata.clear-stale");
            var marker = p.StaleMarker;
            p.StaleMarker = null;
            return Task.FromResult(marker);
        }

        public Task BeginMaintenanceAsync(Guid runId, CancellationToken cancellationToken)
        {
            p.Record("metadata.begin");
            return Task.CompletedTask;
        }

        public Task<CompatibilityVersions?> AdvanceAsync(
            Guid runId, IReadOnlyList<MigrationRunRecord> runs, CancellationToken cancellationToken)
        {
            p.Record("metadata.advance");
            p.AdvancedWith = runs;
            return Task.FromResult(CompatibilityMetadataWriter.Compute(runs));
        }

        public Task EndMaintenanceAsync(Guid runId, CancellationToken cancellationToken)
        {
            p.Record("metadata.end");
            return Task.CompletedTask;
        }
    }

    private sealed class JournalDouble(RecordingPorts p) : IServerMigrationJournal
    {
        public Task OpenAsync(ServerMigrationRun run, CancellationToken cancellationToken)
        {
            p.Record("journal.open");
            p.OpenedRun = run;
            return Task.CompletedTask;
        }

        public Task CloseAsync(Guid runId, MigrationOutcome outcome, string? failureCause,
            IReadOnlyList<string> appliedAfter, CancellationToken cancellationToken)
        {
            p.Record("journal.close");
            p.Closed = (outcome, failureCause, appliedAfter);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MigrationRunRecord>> ReadRunsAsync(Guid currentRunId, CancellationToken cancellationToken)
        {
            p.Record("journal.runs");
            return Task.FromResult<IReadOnlyList<MigrationRunRecord>>(p.PreviousRuns.ToArray());
        }
    }

    private sealed class GrantsDouble(RecordingPorts p) : IApplicationRoleGrants
    {
        public Task<bool> RoleExistsAsync(string role, CancellationToken cancellationToken)
        {
            p.Record("grants.exists");
            return Task.FromResult(p.RoleExists);
        }

        public Task GrantAsync(string role, CancellationToken cancellationToken)
        {
            p.Record("grants.grant");
            p.GrantedRole = role;
            return Task.CompletedTask;
        }
    }

    private sealed class VerificationDouble(RecordingPorts p) : IServerSchemaVerification
    {
        public Task<SchemaVerificationResult> VerifyAsync(string appRole, CancellationToken cancellationToken)
        {
            p.Record("verify");
            return Task.FromResult(p.VerificationFailure is null
                ? SchemaVerificationResult.Success
                : SchemaVerificationResult.Failed(p.VerificationFailure));
        }
    }
}
