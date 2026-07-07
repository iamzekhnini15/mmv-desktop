namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Ligne d'historique de commandes d'un produit (P2D-7D), consommée par
/// <c>ProductDetailViewModel.OrderHistory</c> / <c>ProductOrderHistoryView</c>. Reflet plat de
/// <c>MMV.Domain.Entities.OrderItem</c> (aucune navigation EF).
/// </summary>
public sealed class ProductOrderHistoryItemDto
{
    /// <summary>Quantité commandée.</summary>
    public int Quantity { get; init; }

    /// <summary>Prix unitaire de l'article.</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Commande parente (numéro, date, statut, client), ou <c>null</c> si non chargée.</summary>
    public ProductOrderHistoryOrderDto? Order { get; init; }
}
