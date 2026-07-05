namespace MMV.Application.UseCases.Products.CreateProduct;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateProductUseCase"/>.
/// </summary>
public sealed class CreateProductResult
{
    /// <summary>Identifiant attribué au produit créé.</summary>
    public long ProductId { get; init; }
}
