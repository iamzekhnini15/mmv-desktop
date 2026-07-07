using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Products.ListProducts;

/// <summary>
/// Commande parente d'une ligne d'historique produit (P2D-7D) : <c>Order.OrderNumber</c> / <c>OrderDate</c> /
/// <c>Status</c> (<c>ProductOrderHistoryView</c>).
/// </summary>
public sealed class ProductOrderHistoryOrderDto
{
    /// <summary>Numéro de commande.</summary>
    public string OrderNumber { get; init; } = string.Empty;

    /// <summary>Date de création de la commande.</summary>
    public DateTime OrderDate { get; init; }

    /// <summary>Statut de la commande dans le workflow.</summary>
    public OrderStatus Status { get; init; }

    /// <summary>Vente parente (porteuse du client), ou <c>null</c> si non chargée. Conserve le chemin
    /// de binding <c>Order.Sale.Customer</c> attendu par <c>ProductOrderHistoryView</c>.</summary>
    public ProductOrderHistorySaleDto? Sale { get; init; }
}
