namespace MMV.Application.UseCases.Products.UpdateProduct;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateProductUseCase"/>.
/// </summary>
public sealed class UpdateProductResult
{
    /// <summary>Indique si le produit visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool ProductFound { get; init; }

    /// <summary>Identifiant du produit visé (écho de l'entrée).</summary>
    public long ProductId { get; init; }
}
