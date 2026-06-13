using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Ligne d'entrée du use case <see cref="RegisterSaleUseCase"/> : porte les données nécessaires à la
/// création d'un <see cref="MMV.Domain.Entities.SaleItem"/> (et, pour les verres, d'un
/// <see cref="MMV.Domain.Entities.OrderItem"/> de commande fournisseur), y compris les paramètres optiques
/// OD/OG actuellement renseignés par <c>SaleFormViewModel</c>.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia). Le type d'article brut (<see cref="ItemType"/>) est transmis
/// tel quel par la ViewModel ; le use case applique la même normalisation que le flux d'origine
/// (Frame / LensOd / LensOg, sinon Accessory). Aucun nouveau concept métier, aucun <c>Money</c>, aucune devise.
/// </remarks>
public sealed class RegisterSaleLineCommand
{
    /// <summary>Identifiant du produit (optionnel, comme dans le flux d'origine).</summary>
    public long? ProductId { get; init; }

    /// <summary>Type d'article brut (Monture, Verre OD, Verre OG, Accessoire…) issu du panier.</summary>
    public OrderItemType ItemType { get; init; }

    /// <summary>Quantité vendue.</summary>
    public int Quantity { get; init; }

    /// <summary>Prix unitaire de la ligne.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Type d'usage du verre (Distance, Près, Progressif).</summary>
    public LensUsageType? UsageType { get; init; }

    /// <summary>Sphère de correction (dioptrie).</summary>
    public double? Sphere { get; init; }

    /// <summary>Cylindre de correction (astigmatisme).</summary>
    public double? Cylinder { get; init; }

    /// <summary>Axe de correction (0-180 degrés).</summary>
    public int? Axis { get; init; }

    /// <summary>Addition (presbytie).</summary>
    public double? Addition { get; init; }

    /// <summary>Valeur du prisme (dioptries prismatiques).</summary>
    public double? PrismValue { get; init; }

    /// <summary>Base du prisme (orientation).</summary>
    public PrismBase? PrismBase { get; init; }

    /// <summary>Acuité visuelle (format : 10/10, 8/10, etc.).</summary>
    public string? VisualAcuity { get; init; }
}
