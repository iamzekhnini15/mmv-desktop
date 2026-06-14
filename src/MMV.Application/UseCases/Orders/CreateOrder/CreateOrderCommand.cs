using System;
using System.Collections.Generic;

namespace MMV.Application.UseCases.Orders.CreateOrder;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreateOrderUseCase"/> — « Créer une commande fournisseur ». Contient
/// uniquement les données nécessaires provenant de <c>OrderFormViewModel</c> ; la ViewModel transforme son état
/// (champs liés à l'UI) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel du flux de création
/// (<c>SaveAsync</c>, branche création) : le use case ne recalcule rien et n'introduit aucun nouveau concept
/// (aucun <c>Money</c>, aucune devise, aucun fournisseur/pays).
/// <para>
/// <see cref="OrderNumber"/> est attribué <b>à l'ouverture du formulaire</b> par le ViewModel via
/// <c>INumberSequenceService</c> (séquence <c>ORDER</c>, comportement P2A-1E affiché en lecture seule) et
/// transmis ici tel quel : la numérotation reste à l'ouverture (préservée), la persistance est déléguée au
/// use case.
/// </para>
/// </remarks>
public sealed class CreateOrderCommand
{
    /// <summary>Numéro de commande déjà attribué par la ViewModel (séquence <c>ORDER</c>), ex. <c>CMD-000007</c>.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Date estimée de réception des verres (telle que saisie/initialisée par la ViewModel).</summary>
    public DateTime? EstimatedDelivery { get; init; }

    /// <summary>Notes libres de la commande (vidées vers <c>null</c> si blanches, comme le flux d'origine).</summary>
    public string? Notes { get; init; }

    /// <summary>Lignes de la commande (articles, paramètres optiques OD/OG inclus).</summary>
    public IReadOnlyList<CreateOrderLineCommand> Lines { get; init; } = new List<CreateOrderLineCommand>();
}
