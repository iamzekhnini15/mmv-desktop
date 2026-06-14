using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.CreateOrder;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateOrderUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (notification, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// Le flux d'origine ne lit aucune valeur de retour (l'événement <c>OrderSaved</c> est sans charge utile) ; ce
/// résultat expose néanmoins les informations clés de la commande créée pour préparer l'évolution future, sans
/// faire transiter l'entité ni aucun type EF.
/// </remarks>
public sealed class CreateOrderResult
{
    /// <summary>Identifiant de la commande fournisseur créée.</summary>
    public long OrderId { get; init; }

    /// <summary>Numéro de commande attribué (séquence <c>ORDER</c>), ex. <c>CMD-000007</c>.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Statut initial de la commande (<c>New</c>, comme le flux d'origine).</summary>
    public OrderStatus Status { get; init; }

    /// <summary>Date estimée de réception des verres.</summary>
    public DateTime? EstimatedDelivery { get; init; }
}
