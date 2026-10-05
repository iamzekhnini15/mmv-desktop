using System.Security.Cryptography;
using MMV.DatabaseManager.Backup;

namespace MMV.DatabaseManager.Tests.Backup;

/// <summary>
/// Sauvegarde factice écrite par les écrivains de production (manifeste, fichier, preuve) dans un dossier
/// temporaire — pour les contrôles <b>sans serveur</b>. La restauration réelle est prouvée en intégration.
/// </summary>
internal sealed class TemporaryBackup : IDisposable
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    private TemporaryBackup(string directory, BackupManifest manifest)
    {
        Directory = directory;
        Manifest = manifest;
        var stem = BackupFiles.Stem(manifest.BackupId, CreatedAt);
        ManifestPath = Path.Combine(directory, stem + BackupFiles.ManifestSuffix);
        DumpPath = Path.Combine(directory, manifest.DumpFile);
        ProofPath = BackupFiles.ProofPathFor(ManifestPath);
    }

    public string Directory { get; }
    public BackupManifest Manifest { get; private set; }
    public string ManifestPath { get; }
    public string DumpPath { get; }
    public string ProofPath { get; }

    public static DatabaseFingerprint State(DateTimeOffset? at = null) => new(
        "7400000000000000001", "mmv", 16384, at ?? CreatedAt,
        ["20260101000000_InitialPostgreSqlBaseline"],
        [new TableRowCount("mmv_meta", "migration_run", 1), new TableRowCount("public", "Customers", 12),
         new TableRowCount("public", "__EFMigrationsHistory", 1)]);

    /// <summary>Sauvegarde complète ; <paramref name="withProof"/> ⇒ preuve de vérification cohérente.</summary>
    public static async Task<TemporaryBackup> CreateAsync(bool withProof = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"mmv-backup-unit-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);

        var id = Guid.NewGuid();
        var dump = RandomNumberGenerator.GetBytes(4096);
        var stem = BackupFiles.Stem(id, CreatedAt);
        await File.WriteAllBytesAsync(Path.Combine(directory, stem + BackupFiles.DumpSuffix), dump);

        var manifest = new BackupManifest(id, "1.0.0", "OP-UNIT", "mmv_backup", "17.10", State(),
            stem + BackupFiles.DumpSuffix, dump.Length, Convert.ToHexStringLower(SHA256.HashData(dump)));
        var backup = new TemporaryBackup(directory, manifest);
        await BackupFiles.WriteManifestAsync(backup.ManifestPath, manifest, CancellationToken.None);
        if (withProof)
        {
            await backup.WriteProofAsync();
        }

        return backup;
    }

    public async Task WriteProofAsync(Func<BackupProof, BackupProof>? alter = null)
    {
        var (_, manifestSha256) = await BackupFiles.ReadManifestAsync(ManifestPath, CancellationToken.None);
        var proof = new BackupProof(Manifest.BackupId, manifestSha256, Manifest.DumpSha256, CreatedAt.AddMinutes(5),
            "postgres", "OP-UNIT", "1.0.0", Manifest.State.SystemIdentifier, "mmv_restore_check_x", 3, 14);
        await BackupFiles.WriteProofAsync(ProofPath, alter is null ? proof : alter(proof), CancellationToken.None);
    }

    /// <summary>Réécrit le manifeste (sans toucher à la preuve).</summary>
    public async Task RewriteManifestAsync(Func<BackupManifest, BackupManifest> alter)
    {
        Manifest = alter(Manifest);
        await BackupFiles.WriteManifestAsync(ManifestPath, Manifest, CancellationToken.None);
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
