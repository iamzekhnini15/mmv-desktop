namespace MMV.DatabaseManager;

/// <summary>
/// Codes de sortie de l'outil (P4-6B, §2.2 du plan) : un échec doit être diagnosticable sans lire un journal.
/// Les valeurs sont figées par test.
/// </summary>
public enum MigrationExitCode
{
    /// <summary>Succès ; ou <c>status</c> exécuté.</summary>
    Success = 0,

    /// <summary>Arguments invalides : opérateur absent, <c>--app-role</c> absent ou rôle inexistant.</summary>
    InvalidArguments = 10,

    /// <summary>Sauvegarde absente ou non vérifiée (H12).</summary>
    BackupNotVerified = 11,

    /// <summary>Verrou non obtenu : une autre exécution est en cours (H2).</summary>
    LockNotAcquired = 12,

    /// <summary>Échec de migration ; base à la dernière migration réussie.</summary>
    MigrationFailed = 13,

    /// <summary>Vérification post-migration en échec, y compris droits du rôle applicatif non assurés (Q-23).</summary>
    VerificationFailed = 14,

    /// <summary>Métadonnée absente ou incohérente ; <c>adopt-compatibility</c> requis (Q-22).</summary>
    MetadataInconsistent = 15,

    /// <summary>
    /// P4-8 : refus de sécurité ou d'état — serveur non conforme (TLS, SCRAM, pg_hba), rôle existant privilégié,
    /// base existante d'un autre propriétaire, identité privilégiée configurée sur un poste.
    /// </summary>
    SecurityRefused = 16,

    /// <summary>P4-8 : premier administrateur déjà créé, ou schéma non initialisé (bootstrap one-shot, D-12).</summary>
    BootstrapRefused = 17,

    /// <summary>P4-8 : provisioning ou rotation interrompus par une erreur serveur ; relance idempotente.</summary>
    ProvisioningFailed = 18,

    /// <summary>Serveur injoignable ou authentification refusée.</summary>
    ServerUnreachable = 20
}
