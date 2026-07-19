namespace MMV.Domain.Enums;

/// <summary>
/// Résultat du <b>contrôle qualité atelier</b> d'une version de fiche (P3-6B).
/// </summary>
/// <remarks>
/// <para>
/// Machine à états volontairement minimale : une version naît <see cref="Pending"/> et ne peut prendre
/// qu'<b>une seule</b> décision définitive — <see cref="Pending"/> → <see cref="Passed"/> ou
/// <see cref="Pending"/> → <see cref="Failed"/>. Aucun retour en arrière, aucune réouverture : une reprise
/// atelier crée une <b>nouvelle version</b> qui repart <see cref="Pending"/>.
/// </para>
/// <para>
/// À ne pas confondre avec <see cref="OrderStatus.QualityCheck"/>, qui est une étape du <i>workflow de
/// commande</i> (où en est la commande) et n'exprime aucun <i>résultat</i> de contrôle.
/// </para>
/// </remarks>
public enum WorkshopSheetQcStatus
{
    /// <summary>Contrôle non encore effectué (état initial de toute nouvelle version).</summary>
    Pending,

    /// <summary>Contrôle validé : la fabrication est conforme.</summary>
    Passed,

    /// <summary>Contrôle refusé : un défaut a été constaté (commentaire obligatoire).</summary>
    Failed
}
