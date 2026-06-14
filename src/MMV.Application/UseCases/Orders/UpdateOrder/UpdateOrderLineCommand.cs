using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.UpdateOrder;

/// <summary>
/// Ligne d'entrée du use case <see cref="UpdateOrderUseCase"/> : porte les données nécessaires à la
/// reconstruction d'un <see cref="MMV.Domain.Entities.OrderItem"/> lors de l'édition d'une commande existante,
/// y compris les paramètres optiques (sphère / cylindre / axe / addition) renseignés par
/// <c>OrderFormViewModel</c>.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia). Le type d'article brut (<see cref="ItemType"/>) est transmis tel
/// quel par la ViewModel ; le use case applique la même règle que le flux d'origine (paramètres optiques
/// conservés uniquement pour les verres OD/OG, sinon ignorés). Aucun nouveau concept métier, aucun <c>Money</c>,
/// aucune devise. Structurellement identique à <c>CreateOrderLineCommand</c> : la branche édition reconstruit
/// les lignes exactement comme la branche création.
/// </remarks>
public sealed class UpdateOrderLineCommand
{
    /// <summary>Identifiant du produit sélectionné (toujours renseigné : la validation d'entrée UI l'exige).</summary>
    public long ProductId { get; init; }

    /// <summary>Type d'article brut (Monture, Verre OD, Verre OG, Accessoire) issu du formulaire.</summary>
    public OrderItemType ItemType { get; init; }

    /// <summary>Quantité commandée.</summary>
    public int Quantity { get; init; }

    /// <summary>Prix unitaire de la ligne.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Sphère de correction (dioptrie) — conservée pour les verres uniquement.</summary>
    public double? Sphere { get; init; }

    /// <summary>Cylindre de correction (astigmatisme) — conservé pour les verres uniquement.</summary>
    public double? Cylinder { get; init; }

    /// <summary>Axe de correction (0-180 degrés) — conservé pour les verres uniquement.</summary>
    public int? Axis { get; init; }

    /// <summary>Addition (presbytie) — conservée pour les verres uniquement.</summary>
    public double? Addition { get; init; }
}
