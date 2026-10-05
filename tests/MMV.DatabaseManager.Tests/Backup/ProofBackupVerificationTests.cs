using System.Text;
using FluentAssertions;
using MMV.DatabaseManager.Backup;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// P4-9 — premier temps de la vérification (avant le verrou, sans serveur) : manifeste conforme, fichier intact,
/// preuve de restauration réelle désignant exactement ce manifeste et ce fichier. Les documents sont écrits par
/// les écrivains de production.
/// </summary>
public sealed class ProofBackupVerificationTests
{
    private static readonly ProofBackupVerification Verifier = new(BackupPolicy.MaximumAgeBeforeMigration);

    private static Task<BackupVerificationResult> Verify(TemporaryBackup backup) =>
        Verifier.VerifyAsync(backup.ManifestPath, CancellationToken.None);

    [Fact]
    public async Task Valid_backup_with_proof_is_verified_and_carries_its_manifest()
    {
        using var backup = await TemporaryBackup.CreateAsync();

        var result = await Verify(backup);

        result.IsVerified.Should().BeTrue(result.Reason);
        result.Manifest.Should().BeEquivalentTo(backup.Manifest);
    }

    [Fact]
    public async Task Backup_without_proof_is_not_verified()
    {
        using var backup = await TemporaryBackup.CreateAsync(withProof: false);

        var result = await Verify(backup);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("non vérifiée").And.Contain("verify-backup");
    }

    [Fact]
    public async Task Missing_manifest_is_refused()
    {
        var result = await Verifier.VerifyAsync(
            Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}{BackupFiles.ManifestSuffix}"), CancellationToken.None);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("introuvable");
    }

    [Fact]
    public async Task Corrupted_backup_file_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        var bytes = await File.ReadAllBytesAsync(backup.DumpPath);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(backup.DumpPath, bytes);

        var result = await Verify(backup);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("altéré ou incomplet");
    }

    [Fact]
    public async Task Incomplete_backup_file_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        var bytes = await File.ReadAllBytesAsync(backup.DumpPath);
        await File.WriteAllBytesAsync(backup.DumpPath, bytes[..^1]);

        (await Verify(backup)).Reason.Should().Contain("altéré ou incomplet");
    }

    [Fact]
    public async Task Missing_backup_file_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        File.Delete(backup.DumpPath);

        (await Verify(backup)).Reason.Should().Contain("introuvable");
    }

    [Fact]
    public async Task Manifest_changed_after_its_verification_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await backup.RewriteManifestAsync(m => m with { Operator = "OP-OTHER" });

        var result = await Verify(backup);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("le manifeste a changé");
    }

    [Fact]
    public async Task Incorrect_checksum_in_the_manifest_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await backup.RewriteManifestAsync(m => m with { DumpSha256 = new string('0', 64) });

        (await Verify(backup)).Reason.Should().Contain("altéré ou incomplet");
    }

    [Fact]
    public async Task Backup_file_replaced_after_verification_with_a_consistent_manifest_is_refused()
    {
        // Fichier ET manifeste remplacés de façon cohérente : seule la preuve les distingue.
        using var backup = await TemporaryBackup.CreateAsync();
        var other = Encoding.UTF8.GetBytes(new string('x', 4096));
        await File.WriteAllBytesAsync(backup.DumpPath, other);
        await backup.RewriteManifestAsync(m => m with
        {
            DumpSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(other)),
            DumpSizeBytes = other.Length
        });

        (await Verify(backup)).IsVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Proof_of_another_backup_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await backup.WriteProofAsync(p => p with { BackupId = Guid.NewGuid() });

        (await Verify(backup)).Reason.Should().Contain("autre sauvegarde");
    }

    [Fact]
    public async Task Proof_with_another_dump_checksum_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await backup.WriteProofAsync(p => p with { DumpSha256 = new string('a', 64) });

        (await Verify(backup)).Reason.Should().Contain("le fichier de sauvegarde a changé");
    }

    [Fact]
    public async Task Proof_older_than_the_backup_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        await backup.WriteProofAsync(p => p with { VerifiedAtServerUtc = TemporaryBackup.CreatedAt.AddSeconds(-1) });

        (await Verify(backup)).Reason.Should().Contain("antérieure");
    }

    [Theory]
    [InlineData("\"result\": \"verified\"", "\"result\": \"failed\"")]
    [InlineData("\"formatVersion\": 1", "\"formatVersion\": 2")]
    [InlineData("\"format\": \"mmv-backup-verification\"", "\"format\": \"other\"")]
    public async Task Altered_proof_document_is_refused(string from, string to)
    {
        using var backup = await TemporaryBackup.CreateAsync();
        var text = await File.ReadAllTextAsync(backup.ProofPath);
        text.Should().Contain(from);
        await File.WriteAllTextAsync(backup.ProofPath, text.Replace(from, to));

        var result = await Verify(backup);

        result.IsVerified.Should().BeFalse();
        result.Reason.Should().Contain("document de sauvegarde refusé");
    }

    [Fact]
    public async Task Verified_backup_is_confirmed_only_against_the_same_unchanged_database_within_age()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        var verified = await Verify(backup);
        var live = TemporaryBackup.State(TemporaryBackup.CreatedAt.AddMinutes(30));

        Verifier.ConfirmCurrent(verified, live).IsVerified.Should().BeTrue();
        Verifier.ConfirmCurrent(verified, live with { SystemIdentifier = "1" }).Reason.Should().Contain("autre installation");
        Verifier.ConfirmCurrent(verified, live with { DatabaseOid = 1 }).Reason.Should().Contain("autre base");
        Verifier.ConfirmCurrent(verified, live with { AppliedMigrations = [] }).Reason.Should().Contain("historique");
        Verifier.ConfirmCurrent(verified, live with { Tables = [] }).Reason.Should().Contain("la base a changé");
        Verifier.ConfirmCurrent(verified, live with { ServerTimeUtc = TemporaryBackup.CreatedAt + BackupPolicy.MaximumAgeBeforeMigration + TimeSpan.FromSeconds(1) })
            .Reason.Should().Contain("trop ancienne");
    }

    [Fact]
    public async Task Manifest_that_is_not_named_as_a_manifest_is_refused()
    {
        using var backup = await TemporaryBackup.CreateAsync();
        var renamed = Path.Combine(backup.Directory, "backup.json");
        File.Copy(backup.ManifestPath, renamed);

        (await Verifier.VerifyAsync(renamed, CancellationToken.None)).IsVerified.Should().BeFalse();
    }
}
