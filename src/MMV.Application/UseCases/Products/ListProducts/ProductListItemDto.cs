using System;
using System.Collections.Generic;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// DTO applicatif (lecture seule) d'un produit (P2D-7D). Remplace l'entité EF <c>Product</c> côté UI dans
/// <c>ProductsListViewModel</c> (liste), <c>ProductDetailViewModel</c> (fiche + historique de commandes) et
/// <c>ProductFormViewModel</c> (pré-remplissage du formulaire d'édition, tous catégories confondues). Le même
/// objet circule à travers ces trois écrans (comme l'entité d'origine), porteur des sous-DTO de détail par
/// catégorie et de l'historique de commandes.
/// </summary>
public sealed class ProductListItemDto
{
    /// <summary>Identifiant unique du produit.</summary>
    public long ProductId { get; init; }

    /// <summary>Référence unique du produit.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Désignation/nom commercial du produit.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Description détaillée du produit.</summary>
    public string? Description { get; init; }

    /// <summary>Catégorie du produit.</summary>
    public ProductCategoryEnum Category { get; init; }

    /// <summary>Identifiant du fournisseur.</summary>
    public long SupplierId { get; init; }

    /// <summary>Prix d'achat unitaire.</summary>
    public decimal PurchasePrice { get; init; }

    /// <summary>Prix de vente unitaire.</summary>
    public decimal SalePrice { get; init; }

    /// <summary>Prix de vente conseillé.</summary>
    public decimal? RecommendedPrice { get; init; }

    /// <summary>Quantité en stock actuelle.</summary>
    public int StockQuantity { get; init; }

    /// <summary>Seuil d'alerte de stock bas.</summary>
    public int StockAlertThreshold { get; init; }

    /// <summary>Indique si le produit est actif dans le catalogue.</summary>
    public bool IsActive { get; init; }

    /// <summary>Date d'entrée du produit dans le catalogue.</summary>
    public DateTime EntryDate { get; init; }

    /// <summary>Fournisseur de ce produit, ou <c>null</c> si non chargé.</summary>
    public ProductSupplierRefDto? Supplier { get; init; }

    /// <summary>Détails verre (uniquement si <see cref="Category"/> = VERRE), sinon <c>null</c>.</summary>
    public ProductGlassDetailsDto? GlassDetail { get; init; }

    /// <summary>Détails lentille (uniquement si <see cref="Category"/> = LENTILLE), sinon <c>null</c>.</summary>
    public ProductLensDetailsDto? LensDetail { get; init; }

    /// <summary>Détails monture/accessoire, sinon <c>null</c>.</summary>
    public ProductAccessoryDetailsDto? AccessoryDetail { get; init; }

    /// <summary>Historique des commandes contenant ce produit (fiche détaillée).</summary>
    public IReadOnlyList<ProductOrderHistoryItemDto> OrderHistory { get; init; } = Array.Empty<ProductOrderHistoryItemDto>();
}
