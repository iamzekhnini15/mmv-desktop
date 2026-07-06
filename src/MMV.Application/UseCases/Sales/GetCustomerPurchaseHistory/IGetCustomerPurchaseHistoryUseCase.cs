using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;

/// <summary>
/// Query use case « Historique d'achats d'un client » (P2D-5). Remplace <c>ISaleRepository.GetByCustomerIdAsync</c>
/// côté UI (<c>CustomerPurchaseHistoryViewModel</c> et l'onglet Infos de <c>CustomerInfoViewModel</c>).
/// </summary>
public interface IGetCustomerPurchaseHistoryUseCase
{
    /// <summary>
    /// Renvoie les ventes du client (tri décroissant par date, comme le repository), projetées en
    /// <see cref="CustomerSaleItemDto"/> plats (jamais l'entité EF <c>Sale</c>).
    /// </summary>
    Task<IReadOnlyList<CustomerSaleItemDto>> ExecuteAsync(GetCustomerPurchaseHistoryQuery query, CancellationToken cancellationToken = default);
}
