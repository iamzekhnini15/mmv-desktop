using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Article d'une commande détaillée (P2D-7B), utilisé par <c>OrderDetailViewModel.Items</c>,
/// <c>FabricationSheetViewModel</c> (<c>FrameItem</c>/<c>LensOdItem</c>/<c>LensOgItem</c>/<c>Accessories</c>) et le
/// formulaire d'édition (<c>OrderFormViewModel.LoadExistingOrderAsync</c>).
/// </summary>
public sealed class OrderDetailsItemDto
{
    /// <summary>Identifiant unique de l'article de commande.</summary>
    public long OrderItemId { get; init; }

    /// <summary>Identifiant du produit associé (optionnel).</summary>
    public long? ProductId { get; init; }

    /// <summary>Type d'article (Monture, Verre OD, Verre OG, Accessoire).</summary>
    public OrderItemType ItemType { get; init; }

    /// <summary>Quantité commandée.</summary>
    public int Quantity { get; init; }

    /// <summary>Prix unitaire de l'article.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Sphère de correction (verres uniquement).</summary>
    public double? Sphere { get; init; }

    /// <summary>Cylindre de correction (verres uniquement).</summary>
    public double? Cylinder { get; init; }

    /// <summary>Axe de correction (verres uniquement).</summary>
    public int? Axis { get; init; }

    /// <summary>Addition (verres uniquement).</summary>
    public double? Addition { get; init; }

    /// <summary>Produit associé (nom/référence/catégorie pour affichage), ou <c>null</c> si non chargé.</summary>
    public OrderDetailsProductDto? Product { get; init; }
}
