namespace MMV.DatabaseManager.Backup;

/// <summary>
/// <b>Seule</b> implémentation de production de <see cref="IBackupVerification"/> en P4-6B : elle refuse
/// <b>toujours</b> (H12, R-1). Conséquence assumée (décision Q-24) : tant que P4-9 n'a pas livré son
/// vérificateur, <c>migrate</c> et <c>adopt-compatibility</c> refusent sur toute base réelle (code 11).
/// Aucune variable d'environnement, option, clé de configuration ni compilation conditionnelle ne la contourne.
/// </summary>
public sealed class RefusingBackupVerification : IBackupVerification
{
    public Task<BackupVerificationResult> VerifyAsync(string backupReference, CancellationToken cancellationToken) =>
        Task.FromResult(BackupVerificationResult.Refused(
            "Aucune vérification de sauvegarde n'est disponible avant P4-9 : migration refusée (H12)."));
}
