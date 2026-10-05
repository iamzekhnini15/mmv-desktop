using MMV.DatabaseManager.Provisioning;
using MMV.Infrastructure.Configuration;
using MMV.Infrastructure.Data;

namespace MMV.DatabaseManager.Backup;

/// <summary>Sauvegarde vue par la rétention : identifiée par un manifeste lisible.</summary>
public sealed record RetainedBackup(string Stem, DateTimeOffset CreatedAtServerUtc, bool Verified);

/// <summary>
/// Règle de rétention (P4-9, ADR-PROD-DB-011), pure et sans horloge locale : la référence est la sauvegarde la plus
/// récente du dossier. Une sauvegarde est conservée si l'une des conditions tient :
/// <list type="bullet">
///   <item>moins de <see cref="BackupPolicy.DailyRetention"/> avant la référence ;</item>
///   <item>première sauvegarde d'un des <see cref="BackupPolicy.MonthlyRetentionMonths"/> derniers mois civils (UTC) ;</item>
///   <item>vérifiée et de moins de <see cref="BackupPolicy.VerifiedRetentionMonths"/> mois ;</item>
///   <item>dernière sauvegarde vérifiée — <b>toujours</b>, quel que soit son âge.</item>
/// </list>
/// </summary>
public static class BackupRetention
{
    /// <summary>Sauvegardes à supprimer ; jamais aucune si la liste est vide.</summary>
    public static IReadOnlyList<RetainedBackup> ToDelete(IReadOnlyCollection<RetainedBackup> backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        if (backups.Count == 0)
        {
            return [];
        }

        var reference = backups.Max(b => b.CreatedAtServerUtc);
        var keep = new HashSet<string>(StringComparer.Ordinal);

        keep.UnionWith(backups.Where(b => b.CreatedAtServerUtc > reference - BackupPolicy.DailyRetention).Select(b => b.Stem));

        var firstMonth = new DateTimeOffset(reference.Year, reference.Month, 1, 0, 0, 0, TimeSpan.Zero)
            .AddMonths(-(BackupPolicy.MonthlyRetentionMonths - 1));
        keep.UnionWith(backups
            .Where(b => b.CreatedAtServerUtc >= firstMonth)
            .GroupBy(b => (b.CreatedAtServerUtc.Year, b.CreatedAtServerUtc.Month))
            .Select(g => g.OrderBy(b => b.CreatedAtServerUtc).ThenBy(b => b.Stem, StringComparer.Ordinal).First().Stem));

        var verified = backups.Where(b => b.Verified).ToArray();
        keep.UnionWith(verified
            .Where(b => b.CreatedAtServerUtc > reference.AddMonths(-BackupPolicy.VerifiedRetentionMonths))
            .Select(b => b.Stem));
        if (verified.Length > 0)
        {
            keep.Add(verified.MaxBy(b => b.CreatedAtServerUtc)!.Stem);
        }

        return backups.Where(b => !keep.Contains(b.Stem)).OrderBy(b => b.CreatedAtServerUtc).ToArray();
    }
}

/// <summary>
/// Verbe <c>prune-backups</c> (P4-9) : <b>seul</b> chemin de suppression des sauvegardes. Aucun accès serveur,
/// aucun secret. Ne touche qu'aux fichiers nommés par l'outil (<c>mmv-backup-*</c>) :
/// <list type="bullet">
///   <item>sauvegardes hors rétention (<see cref="BackupRetention"/>) — manifeste supprimé <b>en premier</b>, pour
///   qu'une suppression interrompue ne laisse jamais une sauvegarde d'apparence complète ;</item>
///   <item>fichiers orphelins (écriture interrompue, manifeste absent) plus vieux que
///   <see cref="BackupPolicy.OrphanGracePeriod"/> ;</item>
///   <item>un manifeste illisible n'est <b>jamais</b> supprimé, ni ses fichiers : ce qu'on ne sait pas identifier ne
///   se détruit pas — il est signalé.</item>
/// </list>
/// </summary>
public sealed class BackupPruner
{
    private const string Prefix = "mmv-backup-";

    private readonly IMigrationJournal _trace;
    private readonly Func<DateTime> _utcNow;

    public BackupPruner(IMigrationJournal trace, Func<DateTime>? utcNow = null)
    {
        _trace = trace ?? throw new ArgumentNullException(nameof(trace));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<AdministrationResult> RunAsync(string directory, string @operator, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        _trace.Write($"OPEN prune-backups {runId} version {ApplicationVersion.Current} opérateur '{@operator}' dossier '{directory}'");
        var result = await ExecuteAsync(runId, directory, cancellationToken);
        _trace.Write($"CLOSE prune-backups {runId} code {(int)result.ExitCode} {result.Message}");
        return result;
    }

    private async Task<AdministrationResult> ExecuteAsync(Guid runId, string directory, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
        {
            return new(MigrationExitCode.InvalidArguments, "--directory doit désigner un dossier existant (chemin absolu).");
        }

        try
        {
            var backups = new List<RetainedBackup>();
            var identified = new HashSet<string>(StringComparer.Ordinal);
            var unreadable = new List<string>();
            foreach (var manifestPath in Directory.GetFiles(directory, Prefix + "*" + BackupFiles.ManifestSuffix))
            {
                var stem = Path.GetFileName(manifestPath)[..^BackupFiles.ManifestSuffix.Length];
                identified.Add(stem);
                try
                {
                    var (manifest, manifestSha256) = await BackupFiles.ReadManifestAsync(manifestPath, cancellationToken);
                    backups.Add(new RetainedBackup(stem, manifest.State.ServerTimeUtc,
                        await IsVerifiedAsync(manifestPath, manifest, manifestSha256, cancellationToken)));
                }
                catch (BackupDocumentException exception)
                {
                    unreadable.Add(stem);
                    _trace.Write($"prune-backups {runId} : manifeste illisible '{stem}' conservé ({exception.Message})");
                }
            }

            var deleted = 0;
            foreach (var backup in BackupRetention.ToDelete(backups))
            {
                foreach (var suffix in new[] { BackupFiles.ManifestSuffix, BackupFiles.ProofSuffix, BackupFiles.DumpSuffix })
                {
                    File.Delete(Path.Combine(directory, backup.Stem + suffix));
                }

                deleted++;
                _trace.Write($"prune-backups {runId} : sauvegarde '{backup.Stem}' ({backup.CreatedAtServerUtc:o}) supprimée — hors rétention");
            }

            var orphans = 0;
            var limit = _utcNow() - BackupPolicy.OrphanGracePeriod;
            foreach (var file in Directory.GetFiles(directory, Prefix + "*"))
            {
                var name = Path.GetFileName(file);
                var partial = name.EndsWith(BackupFiles.PartialSuffix, StringComparison.Ordinal);
                if ((!partial && (name.EndsWith(BackupFiles.ManifestSuffix, StringComparison.Ordinal) || identified.Contains(StemOf(name))))
                    || File.GetLastWriteTimeUtc(file) > limit)
                {
                    continue;
                }

                File.Delete(file);
                orphans++;
                _trace.Write($"prune-backups {runId} : fichier orphelin '{name}' supprimé (aucun manifeste)");
            }

            return new(MigrationExitCode.Success,
                $"Rétention appliquée : {backups.Count - deleted} sauvegarde(s) conservée(s), {deleted} supprimée(s), " +
                $"{orphans} orphelin(s) supprimé(s), {unreadable.Count} manifeste(s) illisible(s) conservé(s).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(MigrationExitCode.RetentionFailed, $"Rétention en échec : {exception.Message}");
        }
    }

    private static async Task<bool> IsVerifiedAsync(string manifestPath, BackupManifest manifest, string manifestSha256, CancellationToken cancellationToken)
    {
        var proofPath = BackupFiles.ProofPathFor(manifestPath);
        if (!File.Exists(proofPath))
        {
            return false;
        }

        try
        {
            var proof = await BackupFiles.ReadProofAsync(proofPath, cancellationToken);
            return proof.BackupId == manifest.BackupId && proof.ManifestSha256 == manifestSha256 && proof.DumpSha256 == manifest.DumpSha256;
        }
        catch (BackupDocumentException)
        {
            return false;
        }
    }

    /// <summary>Nom commun d'un fichier de sauvegarde : tout ce qui précède le premier point.</summary>
    private static string StemOf(string name) => name.IndexOf('.') is var dot and >= 0 ? name[..dot] : name;
}
