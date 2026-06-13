using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.DeleteOrder;

/// <summary>
/// Port applicatif du use case « Supprimer une commande » (P2B-2H).
/// Consommé par <c>OrdersViewModel</c> ; implémenté dans la couche Application.
/// </summary>
public interface IDeleteOrderUseCase
{
    /// <summary>
    /// Supprime la commande identifiée par <paramref name="command"/>.<see cref="DeleteOrderCommand.OrderId"/>.
    /// </summary>
    Task<DeleteOrderResult> ExecuteAsync(DeleteOrderCommand command, CancellationToken cancellationToken = default);
}
