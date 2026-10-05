namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Vérification de sauvegarde de production (P4-9, DP-10) — <b>contrôle structurel + restauration réelle =
/// sauvegarde vérifiée</b>. La référence (<c>--backup-ref</c>) est le chemin absolu d'un manifeste. Est
/// vérifiée une sauvegarde dont :
/// <list type="number">
///   <item>le manifeste est conforme et le fichier de sauvegarde a exactement la taille et l'empreinte SHA-256
///   annoncées (une sauvegarde tronquée ou altérée est refusée) ;</item>
///   <item>une preuve de vérification existe, désigne cette sauvegarde, ce manifeste <b>octet pour octet</b> et ce
///   fichier : elle atteste qu'il a été réellement restauré et comparé par <c>verify-backup</c>
///   (administrateur) ;</item>
///   <item>sous le verrou, la base courante est la base sauvegardée — même installation, même base, même
///   historique EF, mêmes nombres de lignes — et la sauvegarde n'excède pas l'âge maximal.</item>
/// </list>
/// Le migrateur ne restaure rien et ne reçoit aucun droit pour cela : il vérifie une preuve (DP-5).
/// </summary>
public sealed class ProofBackupVerification : IBackupVerification
{
    private readonly TimeSpan _maximumAge;

    public ProofBackupVerification(TimeSpan maximumAge)
    {
        if (maximumAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge), "L'âge maximal d'une sauvegarde doit être positif.");
        }

        _maximumAge = maximumAge;
    }

    public async Task<BackupVerificationResult> VerifyAsync(string backupReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(backupReference) || !Path.IsPathFullyQualified(backupReference))
        {
            return BackupVerificationResult.Refused("--backup-ref doit être le chemin absolu d'un manifeste de sauvegarde.");
        }

        try
        {
            var (manifest, manifestSha256) = await BackupFiles.ReadManifestAsync(backupReference, cancellationToken);

            var dumpPath = BackupFiles.DumpPathFor(backupReference, manifest);
            if (!File.Exists(dumpPath))
            {
                return BackupVerificationResult.Refused("fichier de sauvegarde introuvable à côté du manifeste.");
            }

            var (dumpSha256, size) = await BackupFiles.HashFileAsync(dumpPath, cancellationToken);
            if (size != manifest.DumpSizeBytes || dumpSha256 != manifest.DumpSha256)
            {
                return BackupVerificationResult.Refused(
                    "fichier de sauvegarde altéré ou incomplet (taille ou empreinte SHA-256 différente du manifeste).");
            }

            var proofPath = BackupFiles.ProofPathFor(backupReference);
            if (!File.Exists(proofPath))
            {
                return BackupVerificationResult.Refused(
                    "sauvegarde non vérifiée : aucune preuve de restauration (lancer verify-backup sous l'identifiant administrateur).");
            }

            var proof = await BackupFiles.ReadProofAsync(proofPath, cancellationToken);
            if (proof.BackupId != manifest.BackupId)
            {
                return BackupVerificationResult.Refused("la preuve de vérification désigne une autre sauvegarde.");
            }

            if (proof.ManifestSha256 != manifestSha256)
            {
                return BackupVerificationResult.Refused("le manifeste a changé depuis sa vérification.");
            }

            if (proof.DumpSha256 != dumpSha256)
            {
                return BackupVerificationResult.Refused("le fichier de sauvegarde a changé depuis sa vérification.");
            }

            if (proof.VerifiedAtServerUtc < manifest.State.ServerTimeUtc)
            {
                return BackupVerificationResult.Refused("preuve de vérification antérieure à la sauvegarde.");
            }

            return BackupVerificationResult.Verified(manifest);
        }
        catch (BackupDocumentException exception)
        {
            return BackupVerificationResult.Refused($"document de sauvegarde refusé : {exception.Message}.");
        }
        catch (IOException exception)
        {
            return BackupVerificationResult.Refused($"lecture de la sauvegarde impossible : {exception.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return BackupVerificationResult.Refused("lecture de la sauvegarde refusée par le système de fichiers.");
        }
    }

    public BackupVerificationResult ConfirmCurrent(BackupVerificationResult verified, DatabaseFingerprint live)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(live);

        if (!verified.IsVerified || verified.Manifest is null)
        {
            return BackupVerificationResult.Refused("aucune sauvegarde vérifiée à rapprocher de la base.");
        }

        var failure = BackupConsistency.CheckCurrent(verified.Manifest, live, _maximumAge);
        return failure is null ? verified : BackupVerificationResult.Refused(failure);
    }
}
