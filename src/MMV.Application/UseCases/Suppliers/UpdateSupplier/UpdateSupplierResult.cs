using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Suppliers.UpdateSupplier;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateSupplierUseCase"/>.
/// </summary>
/// <remarks>
/// <b>P3-9.</b> Extension <b>additive</b> à la convention P3-1 (identique à <c>UpdateProductResult</c>). Les deux
/// axes restent lisibles séparément : <see cref="SupplierFound"/> vaut <c>false</c> uniquement quand le
/// fournisseur visé n'existe pas ; <see cref="IsValid"/> vaut <c>false</c> quand la commande est refusée par la
/// validation. Dans les deux cas, <b>aucune</b> écriture n'a eu lieu et aucune entité suivie n'a été mutée.
/// </remarks>
public sealed class UpdateSupplierResult
{
    /// <summary>Indique si le fournisseur visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool SupplierFound { get; init; }

    /// <summary>Identifiant du fournisseur visé (écho de l'entrée).</summary>
    public long SupplierId { get; init; }

    /// <summary>Erreurs de validation de commande (P3-1/P3-9). <b>Vide</b> = commande valide et modification persistée.</summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si la modification a été persistée).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
