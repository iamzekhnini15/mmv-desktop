using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.AdvanceOrderStatus;

/// <summary>
/// Entrée (DTO) du use case <see cref="AdvanceOrderStatusUseCase"/> — « Faire avancer le statut / réception
/// d'une commande » (troisième vertical slice, P2B-2E). Contient uniquement les données nécessaires provenant
/// de <c>OrderDetailViewModel</c> ; la ViewModel transforme son état (statut courant, transition résolue,
/// libellés d'affichage) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de
/// <c>OrderDetailViewModel.AdvanceStatusAsync</c> : le use case ne recalcule pas la machine à états et
/// n'introduit aucun nouveau concept (aucun <c>Money</c>, aucune devise, aucune règle pays/fiscalité).
/// <para>
/// La <b>machine à états</b> (statut suivant) reste dans la ViewModel car elle pilote aussi, de façon
/// synchrone, l'état d'affichage (libellé du statut suivant, activation du bouton). La transition résolue
/// (<see cref="CurrentStatus"/> → <see cref="NextStatus"/>) est transmise ici ; le use case n'orchestre que la
/// <b>persistance</b> de l'avancement (mise à jour du statut, mouvements de stock de fabrication, notification,
/// enregistrement). Les libellés d'affichage (<see cref="CurrentStatusDisplay"/>, <see cref="NextStatusDisplay"/>,
/// <see cref="CustomerDisplayName"/>) sont calculés par la ViewModel (présentation) et transmis tels quels afin
/// de préserver, au caractère près, le texte de la notification créée par le flux d'origine.
/// </para>
/// </remarks>
public sealed class AdvanceOrderStatusCommand
{
    /// <summary>Identifiant de la commande dont le statut avance.</summary>
    public long OrderId { get; init; }

    /// <summary>Statut courant (avant transition), tel que connu par la ViewModel — sert de « statut précédent ».</summary>
    public OrderStatus CurrentStatus { get; init; }

    /// <summary>Statut cible résolu par la machine à états de la ViewModel.</summary>
    public OrderStatus NextStatus { get; init; }

    /// <summary>Nom d'affichage du client (présentation), repris tel quel dans le message de notification.</summary>
    public string CustomerDisplayName { get; init; } = string.Empty;

    /// <summary>Libellé d'affichage du statut courant (présentation), repris tel quel dans la notification.</summary>
    public string CurrentStatusDisplay { get; init; } = string.Empty;

    /// <summary>Libellé d'affichage du statut cible (présentation), repris tel quel dans la notification.</summary>
    public string NextStatusDisplay { get; init; } = string.Empty;
}
