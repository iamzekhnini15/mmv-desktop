namespace MMV.Application.UseCases.Products.DeleteProduct;

/// <summary>
/// Entrée (DTO) du use case <see cref="DeleteProductUseCase"/> — « Supprimer un produit » (P2C-GLOBAL). Porte
/// l'identifiant du produit sélectionné dans <c>ProductsListViewModel</c>.
/// </summary>
public sealed class DeleteProductCommand
{
    /// <summary>Identifiant du produit à supprimer.</summary>
    public long ProductId { get; init; }
}
