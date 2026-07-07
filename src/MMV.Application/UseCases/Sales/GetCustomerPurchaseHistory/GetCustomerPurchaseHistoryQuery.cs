namespace MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;

/// <summary>
/// Entrée (query) du use case <see cref="GetCustomerPurchaseHistoryUseCase"/> — « Historique d'achats d'un client »
/// (P2D-5). Porte l'unique critère consommé par les écrans clients (<c>CustomerPurchaseHistoryViewModel</c> et
/// l'onglet Infos de <c>CustomerInfoViewModel</c>) : l'identifiant du client. Le tri décroissant par date reste
/// hérité du repository (iso-fonctionnel).
/// </summary>
public sealed class GetCustomerPurchaseHistoryQuery
{
    /// <summary>Identifiant du client dont on charge l'historique de ventes.</summary>
    public long CustomerId { get; init; }
}
