using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.UpdateOrder;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateOrderUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (notification, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// Le flux d'origine ne lit aucune valeur de retour (l'événement <c>OrderSaved</c> est sans charge utile) ; ce
/// résultat expose néanmoins les informations clés de la commande modifiée, sans faire transiter l'entité ni
/// aucun type EF.
/// <para>
/// <see cref="OrderFound"/> indique si la commande visée existait. En mode édition, la ViewModel ouvre
/// toujours le formulaire avec une commande réelle (donc <c>true</c> en pratique) ; ce drapeau couvre le cas
/// théorique d'une commande introuvable sans écriture (cohérent avec le contrat de <c>DeleteOrderUseCase</c>).
/// </para>
/// </remarks>
public sealed class UpdateOrderResult
{
    /// <summary>Indique si la commande à modifier a été trouvée. <c>false</c> = aucune écriture effectuée.</summary>
    public bool OrderFound { get; init; }

    /// <summary>Identifiant de la commande modifiée (écho de l'entrée).</summary>
    public long OrderId { get; init; }

    /// <summary>Numéro de la commande modifiée.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Statut de la commande (inchangé par l'édition, comme le flux d'origine).</summary>
    public OrderStatus Status { get; init; }

    /// <summary>Date estimée de réception des verres après édition.</summary>
    public DateTime? EstimatedDelivery { get; init; }
}
