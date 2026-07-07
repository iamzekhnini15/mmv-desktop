namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Vente parente d'une ligne d'historique produit (P2D-7D). Conserve le chemin de binding d'origine
/// <c>Order.Sale.Customer</c> attendu par <c>ProductOrderHistoryView</c> (aucune navigation EF), sans exposer
/// l'entité <c>Sale</c>.
/// </summary>
public sealed class ProductOrderHistorySaleDto
{
    /// <summary>Client de la vente, ou <c>null</c> si non chargé.</summary>
    public ProductOrderHistoryCustomerDto? Customer { get; init; }
}
