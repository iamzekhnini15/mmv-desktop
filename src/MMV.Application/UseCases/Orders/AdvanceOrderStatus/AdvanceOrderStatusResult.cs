using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.AdvanceOrderStatus;

/// <summary>
/// Sortie (DTO) du use case <see cref="AdvanceOrderStatusUseCase"/>. Permet à la ViewModel de poursuivre son
/// flux (mise à jour de l'état local, rafraîchissement de la liste/Kanban) sans accéder aux détails de
/// persistance.
/// </summary>
/// <remarks>
/// Lorsque la commande est introuvable, <see cref="OrderFound"/> vaut <c>false</c> et <see cref="Order"/> est
/// <c>null</c> : la ViewModel affiche alors « Commande introuvable. » exactement comme le flux d'origine, sans
/// modifier son état. En cas de succès, l'entité <see cref="Order"/> fraîchement rechargée est exposée (transit
/// d'entité toléré pendant la migration, comme <c>RegisterSaleResult.Sale</c>) car la ViewModel doit réafficher
/// la commande mise à jour et propager l'événement <c>OrderUpdated</c> avec cette entité, à l'identique.
/// </remarks>
public sealed class AdvanceOrderStatusResult
{
    /// <summary>Indique si la commande a été retrouvée (sinon : « Commande introuvable. », aucun changement).</summary>
    public bool OrderFound { get; init; }

    /// <summary>Commande fraîchement rechargée (avec ses articles), ou <c>null</c> si introuvable.</summary>
    public Order? Order { get; init; }

    /// <summary>Statut avant transition.</summary>
    public OrderStatus OldStatus { get; init; }

    /// <summary>Statut après transition.</summary>
    public OrderStatus NewStatus { get; init; }

    /// <summary>Vrai si des mouvements de stock de fabrication ont été créés (passage À fabriquer → En fabrication).</summary>
    public bool HasCreatedStockMovements { get; init; }

    /// <summary>Nombre de mouvements de stock créés (0 si aucun).</summary>
    public int CreatedStockMovementCount { get; init; }

    /// <summary>Vrai si une notification de changement de statut a été créée.</summary>
    public bool HasNotification { get; init; }
}
