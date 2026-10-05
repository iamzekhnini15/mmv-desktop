namespace MMV.DatabaseManager.Backup;

/// <summary>
/// Paramètres de politique de sauvegarde (P4-9), déclarés en un seul point. Ce sont des décisions d'architecture,
/// jamais des options de ligne de commande : aucun opérateur ne peut les relâcher au moment de migrer.
/// </summary>
public static class BackupPolicy
{
    /// <summary>
    /// Âge maximal, mesuré sur l'horloge du serveur, d'une sauvegarde vérifiée acceptée par <c>migrate</c>
    /// (procédure §4.6.2 d'ADR-PROD-DB-009 : sauvegarde à l'étape 2, migration à l'étape 4, dans la même fenêtre de
    /// maintenance). Le rapprochement sous verrou exige en outre que la base n'ait pas changé depuis.
    /// </summary>
    public static readonly TimeSpan MaximumAgeBeforeMigration = TimeSpan.FromHours(2);
}
