using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Products.UpdateProduct;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateProductUseCase"/>.
/// </summary>
/// <remarks>
/// <b>P3-4B.</b> Si la commande est refusée (validation métier ou référence en doublon d'un autre produit),
/// <see cref="ValidationErrors"/> est renseignée et <see cref="IsValid"/> vaut <c>false</c> : <b>aucune</b>
/// écriture n'a eu lieu. <see cref="ProductFound"/> reste <c>false</c> uniquement quand le produit visé n'existe pas.
/// </remarks>
public sealed class UpdateProductResult
{
    /// <summary>Indique si le produit visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool ProductFound { get; init; }

    /// <summary>Identifiant du produit visé (écho de l'entrée).</summary>
    public long ProductId { get; init; }

    /// <summary>Erreurs de validation de commande (P3-1/P3-4B). <b>Vide</b> = commande valide et produit mis à jour.</summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si la mise à jour a été persistée).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
