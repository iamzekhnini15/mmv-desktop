using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Products.CreateProduct;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateProductUseCase"/>.
/// </summary>
/// <remarks>
/// <b>P3-4B.</b> Si la commande est refusée (validation métier ou référence en doublon),
/// <see cref="ValidationErrors"/> est renseignée, <see cref="IsValid"/> vaut <c>false</c> et
/// <see cref="ProductId"/> reste à 0 : <b>aucune</b> écriture n'a eu lieu.
/// </remarks>
public sealed class CreateProductResult
{
    /// <summary>Identifiant attribué au produit créé (0 si la commande a été refusée).</summary>
    public long ProductId { get; init; }

    /// <summary>Erreurs de validation de commande (P3-1/P3-4B). <b>Vide</b> = commande valide et produit persisté.</summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si le produit a été persisté).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
