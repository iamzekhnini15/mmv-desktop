using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Détails verre d'un produit (P2D-7D), reflet plat de <c>MMV.Domain.Entities.GlassDetail</c> (aucune navigation
/// EF), consommé par <c>ProductDetailViewModel</c> (fiche) et <c>ProductFormViewModel</c> (pré-remplissage édition).
/// </summary>
public sealed class ProductGlassDetailsDto
{
    /// <summary>Matière du verre.</summary>
    public GlassMaterial? Material { get; init; }

    /// <summary>Type de verre (simple, double, multi-focale).</summary>
    public GlassType? GlassType { get; init; }

    /// <summary>Diamètre du verre.</summary>
    public string? Diameter { get; init; }

    /// <summary>Indice de réfraction.</summary>
    public decimal? Index { get; init; }

    /// <summary>Limite minimale de puissance supportée.</summary>
    public decimal? PowerLimitMin { get; init; }

    /// <summary>Limite maximale de puissance supportée.</summary>
    public decimal? PowerLimitMax { get; init; }
}
