namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Paramètres de politique de sauvegarde (P4-9, décisions d'architecte du 05/10/2026 — ADR-PROD-DB-011), déclarés
/// en un seul point. Ce sont des décisions d'architecture, jamais des options de ligne de commande : aucun
/// opérateur ne peut les relâcher au moment de migrer ou de purger.
/// </summary>
public static class BackupPolicy
{
    /// <summary>Rétention quotidienne : toute sauvegarde de moins de 14 jours (avant la plus récente) est conservée.</summary>
    public static readonly TimeSpan DailyRetention = TimeSpan.FromDays(14);

    /// <summary>Rétention mensuelle : la première sauvegarde de chacun des 12 derniers mois civils (UTC) est conservée.</summary>
    public const int MonthlyRetentionMonths = 12;

    /// <summary>Toute sauvegarde vérifiée (preuve de restauration) des 12 derniers mois est conservée.</summary>
    public const int VerifiedRetentionMonths = 12;

    /// <summary>Délai avant suppression d'un fichier orphelin (écriture interrompue) : jamais un fichier en cours.</summary>
    public static readonly TimeSpan OrphanGracePeriod = TimeSpan.FromHours(24);

    /// <summary>
    /// Âge maximal, mesuré sur l'horloge du serveur, d'une sauvegarde vérifiée acceptée par <c>migrate</c>
    /// (procédure §4.6.2 d'ADR-PROD-DB-009 : sauvegarde à l'étape 2, migration à l'étape 4, dans la même fenêtre de
    /// maintenance). Le rapprochement sous verrou exige en outre que la base n'ait pas changé depuis.
    /// </summary>
    public static readonly TimeSpan MaximumAgeBeforeMigration = TimeSpan.FromHours(2);
}
