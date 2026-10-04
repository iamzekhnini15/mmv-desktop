namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Port de vérification de sauvegarde (P4-6B, H12, DP-10, R-1). Interrogé <b>avant</b> le verrou : un refus
/// n'immobilise jamais la base. Le vérificateur réel est <b>P4-9</b> ; il remplacera
/// <see cref="RefusingBackupVerification"/> dans <c>Program.cs</c> par un commit revu, jamais par configuration.
/// </summary>
public interface IBackupVerification
{
    Task<BackupVerificationResult> VerifyAsync(string backupReference, CancellationToken cancellationToken);
}

/// <summary>Issue d'une vérification de sauvegarde.</summary>
public sealed record BackupVerificationResult(bool IsVerified, string Reason)
{
    public static BackupVerificationResult Verified() => new(true, "sauvegarde vérifiée");

    public static BackupVerificationResult Refused(string reason) => new(false, reason);
}
