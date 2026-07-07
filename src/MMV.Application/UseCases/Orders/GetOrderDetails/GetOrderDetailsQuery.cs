namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Entrée (query) du use case <see cref="GetOrderDetailsUseCase"/> — « Charger la fiche détaillée d'une commande »
/// (P2D-7B). Porte l'unique critère consommé par <c>OrdersViewModel.OnViewOrderDetail</c> : l'identifiant de la
/// commande.
/// </summary>
public sealed class GetOrderDetailsQuery
{
    /// <summary>Identifiant de la commande à recharger avec ses articles.</summary>
    public long OrderId { get; init; }
}
