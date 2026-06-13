using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.AdvanceOrderStatus;

/// <summary>
/// Cas d'utilisation « Faire avancer le statut / réception d'une commande » (troisième vertical slice, P2B-2E).
/// Orchestre la persistance de l'avancement de statut (mise à jour du statut, mouvements de stock de
/// fabrication, notification, enregistrement), en remplacement de l'orchestration métier auparavant portée par
/// <c>OrderDetailViewModel.AdvanceStatusAsync</c>.
/// </summary>
/// <remarks>
/// Périmètre P2B-2E : <b>avancement de statut</b> uniquement. L'encaissement du solde, la checklist contrôle
/// qualité, l'édition, la suppression et la création restent dans les ViewModels / autres use cases et seront
/// migrés ultérieurement (strangler, un flux à la fois).
/// </remarks>
public interface IAdvanceOrderStatusUseCase
{
    /// <summary>
    /// Exécute l'avancement de statut décrit par <paramref name="command"/> et renvoie le résultat (commande
    /// retrouvée ou non, entité rechargée, statuts, mouvements de stock, notification). Les erreurs techniques
    /// sont propagées telles quelles (notamment <see cref="MMV.Domain.Exceptions.PersistenceException"/>),
    /// exactement comme le flux d'origine.
    /// </summary>
    Task<AdvanceOrderStatusResult> ExecuteAsync(AdvanceOrderStatusCommand command, CancellationToken cancellationToken = default);
}
