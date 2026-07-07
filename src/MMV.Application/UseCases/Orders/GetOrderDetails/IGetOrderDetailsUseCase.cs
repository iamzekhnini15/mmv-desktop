using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.GetOrderDetails;

/// <summary>
/// Query use case « Charger la fiche détaillée d'une commande » (P2D-7B). Remplace
/// <c>IOrderRepository.GetWithItemsAsync</c> côté UI (<c>OrdersViewModel.OnViewOrderDetail</c>).
/// </summary>
public interface IGetOrderDetailsUseCase
{
    /// <summary>
    /// Renvoie la commande demandée (avec sa vente, son client et ses articles), projetée en
    /// <see cref="OrderDetailsDto"/> plat/composite (jamais l'entité EF <c>Order</c>), ou <c>null</c> si
    /// introuvable.
    /// </summary>
    Task<OrderDetailsDto?> ExecuteAsync(GetOrderDetailsQuery query, CancellationToken cancellationToken = default);
}
