using System;
using System.Collections.Generic;

namespace MMV.Application.UseCases.Orders.UpdateOrder;

/// <summary>
/// Entrée (DTO) du use case <see cref="UpdateOrderUseCase"/> — « Modifier une commande existante » (P2B-2I).
/// Contient uniquement les données nécessaires provenant de <c>OrderFormViewModel</c> en mode édition ; la
/// ViewModel transforme son état (champs liés à l'UI) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel du flux d'édition
/// (<c>SaveAsync</c>, branche <c>UpdateExistingOrderAsync</c>) : le use case ne recalcule rien et n'introduit
/// aucun nouveau concept (aucun <c>Money</c>, aucune devise, aucun fournisseur/pays).
/// <para>
/// <see cref="OrderId"/> identifie la commande à modifier (chargée par le use case). <see cref="OrderNumber"/>
/// est affiché en lecture seule en mode édition (le formulaire ne le modifie pas) mais est transmis et
/// réaffecté tel quel pour rester strictement fidèle au flux d'origine (qui réaffectait
/// <c>order.OrderNumber = OrderNumber</c>).
/// </para>
/// </remarks>
public sealed class UpdateOrderCommand
{
    /// <summary>Identifiant de la commande à modifier (chargée par le use case).</summary>
    public long OrderId { get; init; }

    /// <summary>Numéro de commande (lecture seule en édition) ; réaffecté tel quel, comme le flux d'origine.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Date estimée de réception des verres (telle que saisie/initialisée par la ViewModel).</summary>
    public DateTime? EstimatedDelivery { get; init; }

    /// <summary>Notes libres de la commande (vidées vers <c>null</c> si blanches, comme le flux d'origine).</summary>
    public string? Notes { get; init; }

    /// <summary>Lignes de la commande (articles, paramètres optiques OD/OG inclus) après édition.</summary>
    public IReadOnlyList<UpdateOrderLineCommand> Lines { get; init; } = new List<UpdateOrderLineCommand>();
}
