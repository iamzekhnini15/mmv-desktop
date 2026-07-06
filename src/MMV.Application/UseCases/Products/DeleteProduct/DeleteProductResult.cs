namespace MMV.Application.UseCases.Products.DeleteProduct;

/// <summary>
/// Sortie (DTO) du use case <see cref="DeleteProductUseCase"/>.
/// </summary>
public sealed class DeleteProductResult
{
    /// <summary>Indique si le produit à supprimer a été trouvé. <c>false</c> = aucune écriture effectuée.</summary>
    public bool ProductFound { get; init; }

    /// <summary>Identifiant du produit visé (écho de l'entrée).</summary>
    public long ProductId { get; init; }
}
