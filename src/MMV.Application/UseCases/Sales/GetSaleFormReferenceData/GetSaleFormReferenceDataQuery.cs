namespace MMV.Application.UseCases.Sales.GetSaleFormReferenceData;

/// <summary>
/// Entrée (query) du use case <see cref="GetSaleFormReferenceDataUseCase"/> — « Charger les données de référence
/// du formulaire de vente » (P2D-7A). Porte l'unique critère consommé par
/// <c>SaleFormViewModel.InitializeForCustomerAsync</c> : l'identifiant du client (pour l'ordonnance active).
/// </summary>
public sealed class GetSaleFormReferenceDataQuery
{
    /// <summary>Identifiant du client pour lequel charger l'ordonnance active.</summary>
    public long CustomerId { get; init; }
}
