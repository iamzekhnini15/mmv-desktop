namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Client affiché sur une ligne d'historique de commandes produit (P2D-7D) : <c>Order.Sale.Customer.FirstName</c> /
/// <c>LastName</c> (<c>ProductOrderHistoryView</c>).
/// </summary>
public sealed class ProductOrderHistoryCustomerDto
{
    /// <summary>Prénom du client.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom de famille du client.</summary>
    public string LastName { get; init; } = string.Empty;
}
