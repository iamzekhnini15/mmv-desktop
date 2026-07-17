namespace MMV.Application.UseCases.Products.SetProductActive;

/// <summary>
/// Entrée (DTO) du use case <see cref="SetProductActiveUseCase"/> — « Activer / désactiver un produit » (P3-4B).
/// Porte l'identifiant et l'état d'activation cible (valeur <b>absolue</b>, jamais un basculement).
/// </summary>
public sealed class SetProductActiveCommand
{
    /// <summary>Identifiant du produit concerné.</summary>
    public long ProductId { get; init; }

    /// <summary>État d'activation cible.</summary>
    public bool IsActive { get; init; }
}
