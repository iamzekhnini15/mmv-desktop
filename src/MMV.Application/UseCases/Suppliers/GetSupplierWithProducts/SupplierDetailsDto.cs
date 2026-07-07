using System;
using System.Collections.Generic;

namespace MMV.Application.UseCases.Suppliers.GetSupplierWithProducts;

/// <summary>
/// DTO applicatif (lecture seule) de la fiche détaillée d'un fournisseur et de ses produits (P2D-2). Remplace
/// l'entité <c>Supplier</c> (et sa navigation EF <c>Products</c>) auparavant renvoyée à <c>SupplierDetailViewModel</c>.
/// </summary>
public sealed class SupplierDetailsDto
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

    /// <summary>Produits fournis (projetés), triés par nom comme à l'affichage.</summary>
    public IReadOnlyList<SupplierProductItemDto> Products { get; init; } = Array.Empty<SupplierProductItemDto>();
}
