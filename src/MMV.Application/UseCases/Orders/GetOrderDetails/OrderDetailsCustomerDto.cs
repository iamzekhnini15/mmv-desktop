namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Résumé du client d'une vente/commande (P2D-7B), utilisé par <c>OrderDetailViewModel.CustomerName</c> et
/// <c>FabricationSheetViewModel.CustomerName</c> / <c>CustomerPhone</c>.
/// </summary>
public sealed class OrderDetailsCustomerDto
{
    /// <summary>Prénom du client.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom de famille du client.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>Numéro de téléphone.</summary>
    public string? Phone { get; init; }
}
