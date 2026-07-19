using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.WorkshopSheets.ListWorkshopSheetVersions;

/// <summary>
/// Implémentation de la query « Lister les versions de fiche atelier d'une commande » (P3-6B).
/// </summary>
/// <remarks>
/// L'empreinte technique de la commande est calculée <b>une seule fois</b> puis comparée à chaque version : une
/// version historique dont l'empreinte diffère est simplement le témoin d'un état passé — c'est normal et attendu,
/// seule l'obsolescence de la version <i>courante</i> appelle une action.
/// </remarks>
public sealed class ListWorkshopSheetVersionsUseCase : IListWorkshopSheetVersionsUseCase
{
    private readonly IOrderRepository _orderRepository;

    public ListWorkshopSheetVersionsUseCase(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    /// <inheritdoc />
    public async Task<ListWorkshopSheetVersionsResult> ExecuteAsync(ListWorkshopSheetVersionsQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var order = await _orderRepository.GetWithItemsAsync(query.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return new ListWorkshopSheetVersionsResult { OrderFound = false };
        }

        var currentFingerprint = WorkshopSheetFingerprint.Compute(order);

        var versions = await _orderRepository
            .GetWorkshopSheetVersionsAsync(query.OrderId, cancellationToken)
            .ConfigureAwait(false);

        var projected = versions
            .Select(v => WorkshopSheetOperations.ToDto(
                v,
                isUpToDate: string.Equals(v.TechnicalFingerprint, currentFingerprint, StringComparison.Ordinal)))
            .ToList();

        return new ListWorkshopSheetVersionsResult
        {
            OrderFound = true,
            Versions = projected
        };
    }
}
