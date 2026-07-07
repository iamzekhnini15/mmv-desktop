using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.GetSaleFormReferenceData;

/// <summary>
/// Détails verre d'un <see cref="SaleProductPickerItemDto"/> (P2D-7A), utilisés par
/// <c>SaleFormViewModel.FilterGlassesByPrescription</c> pour le filtrage de compatibilité (type de verre, limites
/// de puissance). Reflet plat de <c>MMV.Domain.Entities.GlassDetail</c> : aucune navigation EF.
/// </summary>
public sealed class SaleGlassDetailDto
{
    /// <summary>Type de verre (simple, double, multi-focale).</summary>
    public GlassType? GlassType { get; init; }

    /// <summary>Limite minimale de puissance supportée.</summary>
    public decimal? PowerLimitMin { get; init; }

    /// <summary>Limite maximale de puissance supportée.</summary>
    public decimal? PowerLimitMax { get; init; }
}
