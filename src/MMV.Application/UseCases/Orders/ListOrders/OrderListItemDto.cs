using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.ListOrders;

/// <summary>
/// DTO applicatif (lecture seule) d'une commande de fabrication pour les écrans de <b>lecture</b> du module Commandes
/// (P2D-6) : la liste (<c>OrdersListViewModel</c> — filtres statut / recherche / pagination) et la vue Kanban
/// (<c>OrderKanbanViewModel</c> — répartition par statut). Remplace l'entité EF suivie <c>Order</c> côté UI pour ces
/// deux écrans.
/// <para>
/// La forme composite (<see cref="Sale"/> → <see cref="OrderSaleSummaryDto.Customer"/>) reproduit exactement les
/// chemins de liaison XAML existants (<c>Sale.Customer.FirstName</c>, <c>Sale.FinalAmount</c>, …), afin de rester
/// strictement iso-fonctionnel sans toucher aux vues. Ce sont des DTO plats (aucune navigation EF, aucun suivi de
/// contexte), pas des entités : l'invariant « aucune entité EF suivie ne franchit la frontière UI » est respecté.
/// </para>
/// </summary>
/// <remarks>
/// <see cref="Status"/> est l'enum Domain <c>OrderStatus</c> (valeur métier stable, consommée par les convertisseurs
/// de statut et les filtres) — acceptable en DTO : ce n'est pas une entité.
/// </remarks>
public sealed class OrderListItemDto
{
    /// <summary>Identifiant de la commande (ouverture du détail, avancement de statut).</summary>
    public long OrderId { get; init; }

    /// <summary>Numéro de commande (affichage + recherche).</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Statut de workflow (répartition Kanban, filtre liste, convertisseurs d'affichage).</summary>
    public OrderStatus Status { get; init; }

    /// <summary>Date de la commande (tri décroissant hérité des ViewModels).</summary>
    public DateTime OrderDate { get; init; }

    /// <summary>Livraison estimée (colonne d'affichage).</summary>
    public DateTime? EstimatedDelivery { get; init; }

    /// <summary>Notes internes (critère de recherche de la liste).</summary>
    public string? Notes { get; init; }

    /// <summary>Vente rattachée (montants + client), ou <c>null</c> si absente.</summary>
    public OrderSaleSummaryDto? Sale { get; init; }
}
