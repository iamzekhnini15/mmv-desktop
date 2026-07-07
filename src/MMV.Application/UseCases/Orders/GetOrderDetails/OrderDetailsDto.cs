using System;
using System.Collections.Generic;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// DTO applicatif (lecture seule) de la fiche détaillée d'une commande (P2D-7B). Remplace l'entité EF <c>Order</c>
/// côté UI dans <c>OrderDetailViewModel</c>, <c>FabricationSheetViewModel</c> et le formulaire d'édition
/// (<c>OrderFormViewModel</c>). Porte les champs scalaires consommés par ces trois écrans, plus les DTO composites
/// imbriqués (vente, client, articles) nécessaires à leur affichage.
/// </summary>
public sealed class OrderDetailsDto
{
    /// <summary>Identifiant unique de la commande.</summary>
    public long OrderId { get; init; }

    /// <summary>Numéro de commande.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Identifiant de la vente parente.</summary>
    public long SaleId { get; init; }

    /// <summary>Date de création de la commande.</summary>
    public DateTime OrderDate { get; init; }

    /// <summary>Date estimée de réception des verres.</summary>
    public DateTime? EstimatedDelivery { get; init; }

    /// <summary>Statut de la commande dans le workflow.</summary>
    public OrderStatus Status { get; init; }

    /// <summary>Notes relatives à la commande fournisseur.</summary>
    public string? Notes { get; init; }

    /// <summary>Vente parente (montants, client), ou <c>null</c> si non chargée.</summary>
    public OrderDetailsSaleDto? Sale { get; init; }

    /// <summary>Articles (verres/montures/accessoires) de la commande.</summary>
    public IReadOnlyList<OrderDetailsItemDto> Items { get; init; } = Array.Empty<OrderDetailsItemDto>();
}
