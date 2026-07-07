using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.GetInventoryOverview;

/// <summary>
/// DTO applicatif (lecture seule) d'un produit dans l'état d'inventaire (P2D-4). Porte exactement les champs
/// consommés par <c>InventoryViewModel</c> / <c>InventoryItem</c> et l'écran <c>InventoryView</c> :
/// <see cref="Reference"/>, <see cref="Name"/>, <see cref="Category"/> (affichage) et <see cref="StockQuantity"/>
/// (stock théorique). Aucune entité EF ne franchit la frontière UI.
/// </summary>
/// <remarks>
/// <see cref="Category"/> est une enum Domain (valeur métier stable), acceptable en DTO : ce n'est pas une entité.
/// </remarks>
public sealed class InventoryProductItemDto
{
    /// <summary>Identifiant du produit (clé de l'ajustement d'inventaire).</summary>
    public long ProductId { get; init; }

    /// <summary>Référence (code) du produit.</summary>
    public string? Reference { get; init; }

    /// <summary>Nom du produit.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Catégorie du produit (affichage).</summary>
    public ProductCategoryEnum Category { get; init; }

    /// <summary>Stock théorique (quantité en base) servant de référence à la saisie physique.</summary>
    public int StockQuantity { get; init; }
}
