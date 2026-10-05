namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Port de vérification de sauvegarde (P4-6B, H12, DP-10, R-1 ; P4-9). Deux temps :
/// <list type="number">
///   <item><see cref="VerifyAsync"/>, <b>avant</b> le verrou et sans contact serveur : la sauvegarde désignée
///   existe, est intacte et porte une preuve de restauration réelle — un refus n'immobilise jamais la base ;</item>
///   <item><see cref="ConfirmCurrent"/>, <b>sous</b> le verrou et avant toute écriture : la sauvegarde est celle de
///   <b>cette</b> base, dans son état <b>actuel</b>.</item>
/// </list>
/// La seule implémentation de production est <see cref="ProofBackupVerification"/>, construite dans
/// <c>Program.cs</c> ; aucune configuration ne la remplace.
/// </summary>
public interface IBackupVerification
{
    Task<BackupVerificationResult> VerifyAsync(string backupReference, CancellationToken cancellationToken);

    /// <param name="verified">Résultat positif de <see cref="VerifyAsync"/>.</param>
    /// <param name="live">État de la base lu sous le verrou.</param>
    BackupVerificationResult ConfirmCurrent(BackupVerificationResult verified, DatabaseFingerprint live);
}

/// <summary>Issue d'une vérification de sauvegarde ; <see cref="Manifest"/> désigne la sauvegarde vérifiée.</summary>
public sealed record BackupVerificationResult(bool IsVerified, string Reason, BackupManifest? Manifest = null)
{
    public static BackupVerificationResult Verified(BackupManifest? manifest = null) => new(true, "sauvegarde vérifiée", manifest);

    public static BackupVerificationResult Refused(string reason) => new(false, reason);
}
