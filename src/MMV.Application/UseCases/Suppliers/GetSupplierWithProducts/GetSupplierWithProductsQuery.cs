namespace MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

/// <summary>
/// Entrée (query) du use case <see cref="GetSupplierWithProductsUseCase"/> — « Fiche fournisseur + produits »
/// (P2D-2). Porte l'identifiant du fournisseur sélectionné dans la liste.
/// </summary>
public sealed class GetSupplierWithProductsQuery
{
    /// <summary>Identifiant du fournisseur à charger.</summary>
    public long SupplierId { get; init; }
}
