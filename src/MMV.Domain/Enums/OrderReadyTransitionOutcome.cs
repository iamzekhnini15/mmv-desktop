namespace MMV.Domain.Enums;

/// <summary>
/// Résultat de la prise <b>atomique</b> du passage <c>Contrôle qualité → Prête</c> (P3-6B, durcissement).
/// </summary>
/// <remarks>
/// <para>
/// La transition est décidée par une <b>unique</b> instruction conditionnelle qui vérifie, au moment exact de
/// l'écriture, à la fois le statut réellement stocké <b>et</b> l'exigence de fiche atelier. Elle ne peut donc pas
/// être invalidée par une régénération concurrente survenue entre une lecture préalable et l'écriture.
/// </para>
/// <para>
/// Les deux causes d'échec sont distinguées parce qu'elles n'appellent pas le même message : un
/// <see cref="StatusConflict"/> demande de rafraîchir la commande, tandis qu'un
/// <see cref="WorkshopSheetRequirementNotMet"/> désigne une exigence métier de contrôle qualité. Cette
/// distinction est établie <b>après</b> la décision, à titre de diagnostic : elle ne la conditionne jamais.
/// </para>
/// </remarks>
public enum OrderReadyTransitionOutcome
{
    /// <summary>La transition a été prise (une seule ligne affectée).</summary>
    Taken = 0,

    /// <summary>
    /// Le statut réellement stocké n'était plus celui attendu (commande déjà avancée depuis un autre poste, ou
    /// commande introuvable). Aucune écriture.
    /// </summary>
    StatusConflict = 1,

    /// <summary>
    /// Le statut était bon, mais l'exigence de fiche atelier n'était plus satisfaite au moment de l'écriture :
    /// la fiche attendue n'est plus la version courante validée et à jour, ou bien une fiche est apparue alors
    /// que la commande était traitée comme une commande historique sans fiche. Aucune écriture.
    /// </summary>
    WorkshopSheetRequirementNotMet = 2
}
