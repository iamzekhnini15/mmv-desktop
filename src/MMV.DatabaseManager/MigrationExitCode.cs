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
    /// P4-8 : refus de sécurité ou d'état — serveur non conforme (TLS, SCRAM, pg_hba), rôle existant privilégié ou
    /// ayant des membres, base existante d'un autre propriétaire, identité privilégiée configurée sur un poste. Au
    /// provisioning, toujours prononcé au préflight, <b>avant toute écriture</b>.
    /// </summary>
    SecurityRefused = 16,

    /// <summary>P4-8 : premier administrateur déjà créé, ou schéma non initialisé (bootstrap one-shot, D-12).</summary>
    BootstrapRefused = 17,

    /// <summary>
    /// P4-8 : provisioning ou rotation interrompus par une erreur serveur, ou preuve d'authentification du
    /// provisioning en échec après écriture ; relance idempotente.
    /// </summary>
    ProvisioningFailed = 18,

    /// <summary>Serveur injoignable ou authentification refusée.</summary>
    ServerUnreachable = 20,

    /// <summary>P4-9 : sauvegarde en échec (<c>pg_dump</c>, droits, disque) ; aucun manifeste n'est écrit.</summary>
    BackupFailed = 21,

    /// <summary>
    /// P4-9 : vérification de sauvegarde en échec — fichier altéré, contrôle structurel, restauration réelle ou
    /// contenu restauré non conforme, privilège insuffisant ; aucune preuve n'est écrite.
    /// </summary>
    RestoreVerificationFailed = 22,

    /// <summary>P4-9 : application de la rétention interrompue (droits ou erreur du système de fichiers).</summary>
    RetentionFailed = 23,

    /// <summary>
    /// P4-7 : source SQLite refusée — fichier absent ou non fermé, intégrité ou clés étrangères en échec, historique
    /// différent, table ou colonne inconnue porteuse de données, valeur non importable ; <b>aucune écriture</b>.
    /// </summary>
    ImportSourceRejected = 24,

    /// <summary>
    /// P4-7 : base cible refusée — schéma non à jour, métadonnée absente, ou données déjà présentes (import déjà
    /// effectué, premier administrateur créé) ; <b>aucune écriture</b>.
    /// </summary>
    ImportTargetRefused = 25,

    /// <summary>
    /// P4-7 : import interrompu, refusé par le serveur ou vérification avant validation en échec ; la transaction est
    /// annulée et l'état de la cible relu.
    /// </summary>
    ImportFailed = 26
}
