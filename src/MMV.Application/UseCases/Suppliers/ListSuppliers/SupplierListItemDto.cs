using System;
using System.Collections.Generic;
using MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

namespace MMV.Application.UseCases.Suppliers.ListSuppliers;

/// <summary>
/// DTO applicatif (lecture seule) d'une ligne de la liste des fournisseurs (P2D-2). Porte les champs consommés par
/// <c>SuppliersListViewModel</c> (recherche / pagination) et son écran (<c>SuppliersView</c>).
/// </summary>
/// <remarks>
/// <see cref="Products"/> reste <b>vide</b> dans le contexte liste : c'est iso-fonctionnel avec l'existant, où
/// <c>GetAllAsync</c> (AsNoTracking, sans <c>Include</c>) ne chargeait jamais la navigation — le compteur
/// « {0} ref. » affichait donc déjà 0. La collection n'existe que pour préserver la liaison <c>Products.Count</c>.
/// </remarks>
public sealed class SupplierListItemDto
{
    /// <summary>Identifiant du fournisseur.</summary>
    public long SupplierId { get; init; }

    /// <summary>Nom du fournisseur.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Adresse email de contact.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Numéro de téléphone.</summary>
    public string? Phone { get; init; }

    /// <summary>Adresse postale.</summary>
    public string? Address { get; init; }

    /// <summary>Code de référence interne.</summary>
    public string? ReferenceCode { get; init; }

    /// <summary>Toujours vide en contexte liste (compteur d'affichage historique = 0).</summary>
    public IReadOnlyList<SupplierProductItemDto> Products { get; init; } = Array.Empty<SupplierProductItemDto>();
}
