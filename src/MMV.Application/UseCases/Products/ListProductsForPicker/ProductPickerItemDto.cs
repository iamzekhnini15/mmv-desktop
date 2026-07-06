namespace MMV.Application.UseCases.Products.ListProductsForPicker;

/// <summary>
/// DTO applicatif (lecture seule) d'un produit dans un sélecteur du module Stock (P2D-4). Porte les champs consommés
/// par les deux sélecteurs produit : le filtre « produit » de <c>StockMovementsListViewModel</c> (affiche
/// <see cref="Name"/>, filtre sur <see cref="ProductId"/>) et les lignes de <c>StockMovementFormViewModel</c>
/// (affiche <see cref="Reference"/> / <see cref="Name"/> ; recherche sur <see cref="SupplierName"/> /
/// <see cref="Description"/> ; commande construite sur <see cref="ProductId"/>). Aucune entité EF ne franchit la
/// frontière UI.
/// </summary>
public sealed class ProductPickerItemDto
{
    /// <summary>Identifiant du produit.</summary>
    public long ProductId { get; init; }

    /// <summary>Référence (code) du produit.</summary>
    public string? Reference { get; init; }

    /// <summary>Nom du produit.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Nom du fournisseur (critère de recherche du sélecteur de formulaire).</summary>
    public string? SupplierName { get; init; }

    /// <summary>Description du produit (critère de recherche du sélecteur de formulaire).</summary>
    public string? Description { get; init; }
}
