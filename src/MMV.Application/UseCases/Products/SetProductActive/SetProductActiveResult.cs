namespace MMV.Application.UseCases.Products.SetProductActive;

/// <summary>
/// Sortie (DTO) du use case <see cref="SetProductActiveUseCase"/>.
/// </summary>
public sealed class SetProductActiveResult
{
    /// <summary>Indique si le produit visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool ProductFound { get; init; }

    /// <summary>Identifiant du produit visé (écho de l'entrée).</summary>
    public long ProductId { get; init; }

    /// <summary>État d'activation appliqué (écho de l'entrée si trouvé).</summary>
    public bool IsActive { get; init; }
}
