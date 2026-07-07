using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Détails lentille d'un produit (P2D-7D), reflet plat de <c>MMV.Domain.Entities.LensDetail</c> (aucune navigation
/// EF), consommé par <c>ProductDetailViewModel</c> (fiche) et <c>ProductFormViewModel</c> (pré-remplissage édition).
/// </summary>
public sealed class ProductLensDetailsDto
{
    /// <summary>Marque de la lentille.</summary>
    public string? Brand { get; init; }

    /// <summary>Modèle de la lentille.</summary>
    public string? Model { get; init; }

    /// <summary>Matière de la lentille.</summary>
    public LensMaterial? Material { get; init; }

    /// <summary>Type de lentille (unifocal, multifocal, torique).</summary>
    public LensType? LensType { get; init; }

    /// <summary>Diamètre de la lentille.</summary>
    public decimal? Diameter { get; init; }

    /// <summary>Courbure de la lentille.</summary>
    public decimal? BaseCurve { get; init; }

    /// <summary>Indique si la lentille est colorée.</summary>
    public bool IsColored { get; init; }

    /// <summary>Durée d'utilisation de la lentille.</summary>
    public LensDuration? Duration { get; init; }
}
