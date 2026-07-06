using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;

/// <summary>
/// Implémentation du query use case « Historique d'achats d'un client » (P2D-5). Déplace, sans changement de
/// comportement observable, la lecture portée jusque-là par <c>CustomerPurchaseHistoryViewModel.LoadAsync</c> et
/// <c>CustomerInfoViewModel.LoadSalesAsync</c> (<c>ISaleRepository.GetByCustomerIdAsync</c> — tri décroissant par
/// date), en projetant chaque entité <c>Sale</c> vers un <see cref="CustomerSaleItemDto"/> plat.
/// </summary>
public sealed class GetCustomerPurchaseHistoryUseCase : IGetCustomerPurchaseHistoryUseCase
{
    private readonly ISaleRepository _saleRepository;

    public GetCustomerPurchaseHistoryUseCase(ISaleRepository saleRepository)
    {
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CustomerSaleItemDto>> ExecuteAsync(GetCustomerPurchaseHistoryQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var sales = await _saleRepository.GetByCustomerIdAsync(query.CustomerId, cancellationToken);

        return sales.Select(s => new CustomerSaleItemDto
        {
            SaleId = s.SaleId,
            SaleNumber = s.SaleNumber,
            SaleDate = s.SaleDate,
            EstimatedDelivery = s.EstimatedDelivery,
            FinalAmount = s.FinalAmount,
            DepositAmount = s.DepositAmount,
            RemainingAmount = s.RemainingAmount,
            Status = s.Status,
        }).ToList();
    }
}
