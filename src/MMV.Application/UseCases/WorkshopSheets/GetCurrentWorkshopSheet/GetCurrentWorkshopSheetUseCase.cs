using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;

/// <summary>
/// Implémentation de la query « Lire la fiche atelier courante d'une commande » (P3-6B).
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune relecture vivante.</b> Toutes les données affichées proviennent du snapshot de la fiche : ni le
/// client, ni le catalogue ne sont relus pour les reconstituer. La commande n'est chargée que pour <b>une seule</b>
/// raison : recalculer l'empreinte technique et déterminer si la fiche est encore à jour
/// (<c>WorkshopSheetDto.IsUpToDate</c>).
/// </para>
/// </remarks>
public sealed class GetCurrentWorkshopSheetUseCase : IGetCurrentWorkshopSheetUseCase
{
    private readonly IOrderRepository _orderRepository;

    public GetCurrentWorkshopSheetUseCase(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
    }

    /// <inheritdoc />
    public async Task<GetCurrentWorkshopSheetResult> ExecuteAsync(GetCurrentWorkshopSheetQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var order = await _orderRepository.GetWithItemsAsync(query.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return new GetCurrentWorkshopSheetResult { OrderFound = false, SheetFound = false };
        }

        var sheet = await _orderRepository
            .GetCurrentWorkshopSheetAsync(query.OrderId, includeItems: true, cancellationToken)
            .ConfigureAwait(false);

        if (sheet is null)
        {
            return new GetCurrentWorkshopSheetResult { OrderFound = true, SheetFound = false };
        }

        return new GetCurrentWorkshopSheetResult
        {
            OrderFound = true,
            SheetFound = true,
            Sheet = WorkshopSheetOperations.ToDto(sheet, WorkshopSheetOperations.IsUpToDate(sheet, order))
        };
    }
}
