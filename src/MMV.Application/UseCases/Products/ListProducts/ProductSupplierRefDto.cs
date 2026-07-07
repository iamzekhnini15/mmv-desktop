namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Référence fournisseur affichée sur un produit (P2D-7D) : <c>Product.Supplier.Name</c> (liste et fiche produit).
/// </summary>
public sealed class ProductSupplierRefDto
{
    /// <summary>Identifiant du fournisseur.</summary>
    public long SupplierId { get; init; }

    /// <summary>Nom du fournisseur.</summary>
    public string Name { get; init; } = string.Empty;
}
