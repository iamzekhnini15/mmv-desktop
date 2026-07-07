using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Résumé de la vente parente d'une commande (P2D-7B), utilisé par <c>OrderDetailViewModel</c>
/// (<c>TotalAmount</c>, <c>DepositAmount</c>, <c>RemainingAmount</c>, <c>PaymentMethod</c>, <c>CustomerName</c>) et
/// <c>FabricationSheetViewModel</c> (<c>TotalAmount</c>, <c>CustomerName</c>, <c>CustomerPhone</c>).
/// </summary>
public sealed class OrderDetailsSaleDto
{
    /// <summary>Identifiant du client (si renseigné).</summary>
    public long? CustomerId { get; init; }

    /// <summary>Montant final après remise.</summary>
    public decimal FinalAmount { get; init; }

    /// <summary>Montant de l'acompte versé.</summary>
    public decimal? DepositAmount { get; init; }

    /// <summary>Montant restant à payer.</summary>
    public decimal? RemainingAmount { get; init; }

    /// <summary>Méthode de paiement utilisée.</summary>
    public PaymentMethod PaymentMethod { get; init; }

    /// <summary>Client ayant effectué l'achat, ou <c>null</c> si non chargé.</summary>
    public OrderDetailsCustomerDto? Customer { get; init; }
}
