using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.ListProductsForOrderPicker;

/// <summary>
/// DTO applicatif (lecture seule) d'un produit dans le sélecteur d'article du formulaire de commande
/// (<c>OrderFormViewModel</c> / <c>OrderItemLine</c>) — P2D-6. Distinct du <c>ProductPickerItemDto</c> du module
/// Stock (P2D-4) car l'écran de commande consomme en plus le <see cref="SalePrice"/> (report en prix unitaire de la
/// ligne) et la <see cref="Category"/> (filtrage des produits proposés par type d'article). Remplace l'entité EF
/// suivie <c>Product</c> côté sélecteur de commande ; aucune navigation EF n'est exposée.
/// </summary>
/// <remarks>
/// <see cref="Category"/> est l'enum Domain <c>ProductCategoryEnum</c> (valeur métier stable) — acceptable en DTO :
/// ce n'est pas une entité.
/// </remarks>
public sealed class OrderProductPickerDto
{
    /// <summary>Identifiant du produit (construit la ligne de commande).</summary>
    public long ProductId { get; init; }

    /// <summary>Référence (code) du produit (affichage + recherche).</summary>
    public string? Reference { get; init; }

    /// <summary>Nom du produit (affichage + recherche + tri).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Prix de vente (reporté en prix unitaire de la ligne à la sélection).</summary>
    public decimal SalePrice { get; init; }

    /// <summary>Catégorie du produit (filtre des produits proposés selon le type d'article).</summary>
    public ProductCategoryEnum Category { get; init; }
}
