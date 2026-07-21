using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Suppliers.CreateSupplier;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateSupplierUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (fermeture du formulaire, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// <b>P3-9.</b> Extension <b>additive</b> à la convention P3-1 (identique à <c>CreateProductResult</c>) : si la
/// commande est refusée par <see cref="MMV.Domain.Validators.SupplierValidator"/>,
/// <see cref="ValidationErrors"/> est renseignée, <see cref="IsValid"/> vaut <c>false</c> et
/// <see cref="SupplierId"/> reste à 0 — <b>aucune</b> écriture n'a eu lieu.
/// </remarks>
public sealed class CreateSupplierResult
{
    /// <summary>Identifiant attribué au fournisseur créé (0 si la commande a été refusée).</summary>
    public long SupplierId { get; init; }

    /// <summary>Nom du fournisseur créé (écho de l'entrée <b>normalisée</b>).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Erreurs de validation de commande (P3-1/P3-9). <b>Vide</b> = commande valide et fournisseur persisté.</summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si le fournisseur a été persisté).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
