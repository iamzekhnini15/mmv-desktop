using MMV.Domain.Enums;

namespace MMV.Domain.Services;

/// <summary>
/// Politique métier <b>unique</b> de la fiche atelier (P3-6B) : quand une version peut être générée, quelle
/// décision de contrôle qualité est recevable, et quelles conditions débloquent le passage en <c>Prête</c>.
/// </summary>
/// <remarks>
/// <para>
/// Règle pure, sans état ni dépendance (Domain), sur le modèle de <see cref="OrderStatusPolicy"/>. Les messages
/// métier sont exposés en constantes afin que l'Application, les tests — et plus tard l'UI — s'y réfèrent sans
/// dupliquer de littéral (patron P3-2B / P3-3B).
/// </para>
/// </remarks>
public static class WorkshopSheetPolicy
{
    // ---------------------------------------------------------------------------------------------------------
    // Messages métier stables
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Refus de génération : la commande n'est pas dans une étape d'atelier.</summary>
    public const string GenerationStatusMessage =
        "Une fiche atelier ne peut être générée que pour une commande en fabrication ou en contrôle qualité.";

    /// <summary>Refus de génération : la commande ne contient aucune ligne à fabriquer.</summary>
    public const string GenerationWithoutItemsMessage =
        "Une fiche atelier ne peut pas être générée pour une commande sans article.";

    /// <summary>Refus de décision : la version visée n'est plus la version courante.</summary>
    public const string QcNotCurrentVersionMessage =
        "Cette version de fiche atelier n'est plus la version courante : le contrôle qualité a été refusé.";

    /// <summary>Refus de décision : la commande a changé depuis la génération de la fiche.</summary>
    public const string QcObsoleteSheetMessage =
        "Les données techniques de la commande ont changé depuis la génération de cette fiche. " +
        "Générez une nouvelle version avant de valider le contrôle qualité.";

    /// <summary>Refus de décision : une décision de contrôle a déjà été prise sur cette version.</summary>
    public const string QcAlreadyDecidedMessage =
        "Le contrôle qualité de cette fiche atelier a déjà été enregistré et ne peut plus être modifié.";

    /// <summary>Refus de décision : un refus de contrôle exige un commentaire.</summary>
    public const string QcRejectionRequiresCommentMessage =
        "Un refus de contrôle qualité doit être motivé par un commentaire.";

    /// <summary>Refus de transition : la fiche courante n'a pas passé le contrôle qualité.</summary>
    public const string ReadyRequiresPassedQcMessage =
        "Le contrôle qualité de la fiche atelier doit être validé avant de rendre la commande prête.";

    /// <summary>Refus de transition : la fiche courante ne correspond plus à la commande.</summary>
    public const string ReadyRequiresUpToDateSheetMessage =
        "La fiche atelier courante ne correspond plus aux données de la commande. " +
        "Générez une nouvelle version et validez son contrôle qualité avant de rendre la commande prête.";

    /// <summary>
    /// Refus de transition : la fiche vérifiée n'était plus la version courante validée au moment exact de la
    /// prise de statut (régénération concurrente depuis un autre poste).
    /// </summary>
    public const string ReadyRequiresUnchangedCurrentSheetMessage =
        "La fiche atelier de cette commande a été régénérée depuis un autre poste pendant l'opération. " +
        "La commande n'a pas été rendue prête ; rafraîchissez la commande et validez le contrôle qualité de la " +
        "nouvelle version.";

    // ---------------------------------------------------------------------------------------------------------
    // Règles
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Indique si une version de fiche peut être générée pour une commande au statut <paramref name="status"/>.
    /// </summary>
    /// <remarks>
    /// Autorisé en <see cref="OrderStatus.InProgress"/> et <see cref="OrderStatus.QualityCheck"/> — les deux
    /// étapes où l'atelier travaille réellement. Refusé avant (<see cref="OrderStatus.New"/>,
    /// <see cref="OrderStatus.ToFabricate"/> : la fabrication n'a pas commencé, la première version est créée
    /// automatiquement à l'entrée en fabrication) et après (<see cref="OrderStatus.Ready"/>,
    /// <see cref="OrderStatus.Delivered"/> : le travail d'atelier est terminé).
    /// </remarks>
    public static bool CanGenerateForStatus(OrderStatus status)
        => status is OrderStatus.InProgress or OrderStatus.QualityCheck;

    /// <summary>
    /// Indique si une décision de contrôle qualité peut encore être prise sur une version dont l'état est
    /// <paramref name="current"/> — c'est-à-dire uniquement lorsqu'aucune décision n'a été prise.
    /// </summary>
    public static bool CanTakeQcDecision(WorkshopSheetQcStatus current)
        => current == WorkshopSheetQcStatus.Pending;

    /// <summary>
    /// Indique si <paramref name="decision"/> est une décision de contrôle recevable : seules
    /// <see cref="WorkshopSheetQcStatus.Passed"/> et <see cref="WorkshopSheetQcStatus.Failed"/> le sont — on ne
    /// « décide » jamais de repasser en attente.
    /// </summary>
    public static bool IsTerminalQcDecision(WorkshopSheetQcStatus decision)
        => decision is WorkshopSheetQcStatus.Passed or WorkshopSheetQcStatus.Failed;

    /// <summary>
    /// Indique si un commentaire est obligatoire pour <paramref name="decision"/> : un <b>refus</b> doit être
    /// motivé ; une validation peut l'être facultativement.
    /// </summary>
    public static bool RequiresComment(WorkshopSheetQcStatus decision)
        => decision == WorkshopSheetQcStatus.Failed;
}
