using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Stock.ListStockMovements;

/// <summary>
/// DTO applicatif (lecture seule) d'une ligne de la liste des mouvements de stock (P2D-4). Porte exactement les
/// champs consommés par <c>StockMovementsListViewModel</c> (recherche / filtre type + produit / tri / pagination) et
/// son écran (<c>StockMovementsView</c>). Aucune entité EF suivie ne franchit la frontière UI.
/// </summary>
/// <remarks>
/// <see cref="MovementType"/> est une enum Domain (valeur métier stable), acceptable en DTO : ce n'est pas une
/// entité. <see cref="Product"/> est un DTO plat imbriqué (jamais l'entité <c>Product</c>), présent pour préserver
/// les liaisons <c>Product.Name</c> / <c>Product.Reference</c> de la vue.
/// </remarks>
public sealed class StockMovementListItemDto
{
    /// <summary>Identifiant du produit concerné (filtre « produit » de l'écran).</summary>
    public long ProductId { get; init; }

    /// <summary>Référence produit imbriquée (affichage), jamais l'entité EF.</summary>
    public StockMovementProductRefDto Product { get; init; } = new();

    /// <summary>Type de mouvement (Entrée / Sortie / Ajustement).</summary>
    public StockMovementType MovementType { get; init; }

    /// <summary>Quantité déplacée.</summary>
    public int Quantity { get; init; }

    /// <summary>Motif / raison du mouvement (recherche).</summary>
    public string? Reason { get; init; }

    /// <summary>Date et heure du mouvement (tri décroissant).</summary>
    public DateTime CreatedAt { get; init; }
}
