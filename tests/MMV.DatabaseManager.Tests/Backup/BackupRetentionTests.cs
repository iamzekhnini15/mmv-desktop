using System.Security.Cryptography;
using FluentAssertions;
using MMV.DatabaseManager.Backup;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// P4-9 (ADR-PROD-DB-011) — rétention : 14 jours quotidiens, première sauvegarde de chacun des 12 derniers mois,
/// sauvegardes vérifiées de 12 mois, et toujours la dernière vérifiée ; suppression par <c>prune-backups</c> seulement.
/// </summary>
public sealed class BackupRetentionTests
{
    private static readonly DateTimeOffset Reference = new(2026, 10, 5, 22, 0, 0, TimeSpan.Zero);

    private static RetainedBackup At(DateTimeOffset at, bool verified = false) => new($"mmv-backup-{at:yyyyMMddHHmm}", at, verified);

    private static IEnumerable<RetainedBackup> Daily(int days, Func<int, bool>? verified = null) =>
        Enumerable.Range(0, days).Select(d => At(Reference.AddDays(-d), verified?.Invoke(d) ?? false));

    [Fact]
    public void Nothing_is_deleted_from_an_empty_or_recent_directory()
    {
        BackupRetention.ToDelete([]).Should().BeEmpty();
        BackupRetention.ToDelete(Daily(14).ToArray()).Should().BeEmpty();
    }

    [Fact]
    public void Daily_backups_older_than_14_days_are_deleted_except_the_first_of_each_month()
    {
        var backups = Daily(60).ToArray();

        var deleted = BackupRetention.ToDelete(backups);

        deleted.Should().OnlyContain(b => b.CreatedAtServerUtc <= Reference - TimeSpan.FromDays(14));
        var kept = backups.Except(deleted).ToArray();
        kept.Should().Contain(b => b.CreatedAtServerUtc.Month == 9 && b.CreatedAtServerUtc.Day == 1, "première sauvegarde de septembre");
        kept.Should().Contain(b => b.CreatedAtServerUtc == backups.Where(x => x.CreatedAtServerUtc.Month == 8).Min(x => x.CreatedAtServerUtc),
            "première sauvegarde (présente) d'août");
        kept.Count(b => b.CreatedAtServerUtc <= Reference - TimeSpan.FromDays(14)).Should().Be(2, "seules les mensuelles d'août et septembre");
    }

    [Fact]
    public void Monthly_backups_are_kept_for_12_calendar_months_only()
    {
        var monthly = Enumerable.Range(0, 15).Select(m => At(new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero).AddMonths(-m))).ToArray();

        var deleted = BackupRetention.ToDelete(monthly);

        deleted.Select(b => b.CreatedAtServerUtc).Should().BeEquivalentTo(new[]
        {
            new DateTimeOffset(2025, 8, 1, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 9, 1, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 10, 1, 22, 0, 0, TimeSpan.Zero)
        }, "les 12 mois civils retenus vont de novembre 2025 à octobre 2026");
    }

    [Fact]
    public void Verified_backups_are_kept_12_months_and_the_latest_verified_forever()
    {
        var ancientVerified = At(Reference.AddYears(-2).AddDays(10), verified: true);
        var olderVerified = At(Reference.AddYears(-3).AddDays(10), verified: true);
        var midMonthVerified = At(Reference.AddMonths(-5).AddDays(10), verified: true);
        var midMonthPlain = At(Reference.AddMonths(-5).AddDays(11));
        var recent = Daily(3).ToArray();

        var deleted = BackupRetention.ToDelete([ancientVerified, olderVerified, midMonthVerified, midMonthPlain, .. recent]);

        deleted.Should().BeEquivalentTo(new[] { ancientVerified, olderVerified, midMonthPlain },
            "la dernière vérifiée est midMonthVerified ; les vérifiées de plus de 12 mois partent");
    }

    [Fact]
    public void The_latest_verified_backup_is_never_deleted_even_when_very_old()
    {
        var onlyVerified = At(Reference.AddYears(-4).AddDays(9), verified: true);

        BackupRetention.ToDelete([onlyVerified, .. Daily(20)]).Should().NotContain(onlyVerified);
    }
}

/// <summary>P4-9 — <c>prune-backups</c> sur le système de fichiers : seuls les fichiers de l'outil, manifeste d'abord.</summary>
public sealed class BackupPrunerTests : IDisposable
{
    private static readonly DateTimeOffset Reference = new(2026, 10, 5, 22, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Directory.CreateTempSubdirectory("mmv-prune-unit-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private async Task<string> WriteBackupAsync(DateTimeOffset at, bool verified = false)
    {
        var id = Guid.NewGuid();
        var stem = BackupFiles.Stem(id, at);
        var dump = RandomNumberGenerator.GetBytes(64);
        await File.WriteAllBytesAsync(Path.Combine(_directory, stem + BackupFiles.DumpSuffix), dump);
        var manifest = new BackupManifest(id, "1.0.0", "OP", "bkp", "17.10", TemporaryBackup.State(at),
            stem + BackupFiles.DumpSuffix, dump.Length, Convert.ToHexStringLower(SHA256.HashData(dump)));
        var manifestPath = Path.Combine(_directory, stem + BackupFiles.ManifestSuffix);
        await BackupFiles.WriteManifestAsync(manifestPath, manifest, CancellationToken.None);
        if (verified)
        {
            var (_, sha) = await BackupFiles.ReadManifestAsync(manifestPath, CancellationToken.None);
            await BackupFiles.WriteProofAsync(BackupFiles.ProofPathFor(manifestPath),
                new BackupProof(id, sha, manifest.DumpSha256, at.AddMinutes(5), "postgres", "OP", "1.0.0", "1", "x", 1, 1),
                CancellationToken.None);
        }

        return stem;
    }

    private Task<MMV.DatabaseManager.Provisioning.AdministrationResult> PruneAsync(MigrationJournal? trace = null) =>
        new BackupPruner(trace ?? new MigrationJournal(), () => Reference.UtcDateTime).RunAsync(_directory, "OP");

    private string[] Files() => Directory.GetFiles(_directory).Select(Path.GetFileName).ToArray()!;

    [Fact]
    public async Task Out_of_retention_backups_lose_all_their_files_and_recent_ones_keep_theirs()
    {
        var old = await WriteBackupAsync(Reference.AddDays(-40).AddHours(1), verified: false);
        var monthly = await WriteBackupAsync(new DateTimeOffset(2026, 8, 25, 22, 0, 0, TimeSpan.Zero));
        var recent = await WriteBackupAsync(Reference, verified: true);
        var trace = new MigrationJournal();

        var result = await PruneAsync(trace);

        result.ExitCode.Should().Be(MigrationExitCode.Success, result.Message);
        Files().Should().NotContain(f => f.StartsWith(old, StringComparison.Ordinal),
            $"{old} n'est ni quotidienne, ni la première de son mois (août : {monthly}), ni vérifiée");
        Files().Should().Contain(recent + BackupFiles.DumpSuffix).And.Contain(recent + BackupFiles.ProofSuffix);
        trace.Entries.Should().Contain(e => e.Contains(old) && e.Contains("hors rétention"));
    }

    [Fact]
    public async Task Unreadable_manifest_and_its_files_are_never_deleted()
    {
        var stem = await WriteBackupAsync(Reference.AddYears(-3));
        await File.WriteAllTextAsync(Path.Combine(_directory, stem + BackupFiles.ManifestSuffix), "{ corrompu");
        File.SetLastWriteTimeUtc(Path.Combine(_directory, stem + BackupFiles.DumpSuffix), Reference.UtcDateTime.AddYears(-3));
        await WriteBackupAsync(Reference);

        var result = await PruneAsync();

        result.Message.Should().Contain("1 manifeste(s) illisible(s) conservé(s)");
        Files().Should().Contain(stem + BackupFiles.DumpSuffix).And.Contain(stem + BackupFiles.ManifestSuffix);
    }

    [Fact]
    public async Task Old_orphans_are_deleted_young_orphans_and_foreign_files_are_kept()
    {
        await WriteBackupAsync(Reference);
        var oldOrphan = Path.Combine(_directory, "mmv-backup-20260901T220000Z-0000.dump");
        var oldPartial = Path.Combine(_directory, "mmv-backup-20260901T220000Z-0001.dump.partial");
        var youngPartial = Path.Combine(_directory, "mmv-backup-20261005T215900Z-0002.dump.partial");
        var foreign = Path.Combine(_directory, "notes-operateur.txt");
        foreach (var file in new[] { oldOrphan, oldPartial, youngPartial, foreign })
        {
            await File.WriteAllTextAsync(file, "x");
            File.SetLastWriteTimeUtc(file, file == youngPartial ? Reference.UtcDateTime.AddHours(-1) : Reference.UtcDateTime.AddDays(-3));
        }

        var result = await PruneAsync();

        result.Message.Should().Contain("2 orphelin(s)");
        File.Exists(oldOrphan).Should().BeFalse();
        File.Exists(oldPartial).Should().BeFalse();
        File.Exists(youngPartial).Should().BeTrue("une écriture en cours n'est jamais supprimée");
        File.Exists(foreign).Should().BeTrue("seuls les fichiers nommés par l'outil sont concernés");
    }

    [Fact]
    public async Task Missing_or_relative_directory_is_refused()
    {
        (await new BackupPruner(new MigrationJournal()).RunAsync(Path.Combine(_directory, "absent"), "OP"))
            .ExitCode.Should().Be(MigrationExitCode.InvalidArguments);
        AdministrationOptionsParse("prune-backups", "--directory", "relative", "--operator", "OP")
            .Should().Contain("chemin absolu");
    }

    private static string? AdministrationOptionsParse(params string[] args) =>
        MMV.DatabaseManager.CommandLine.AdministrationOptions.Parse(args).Error;
}
