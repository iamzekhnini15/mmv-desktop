using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.CreateOrder;

/// <summary>
/// Cas d'utilisation « Créer une commande fournisseur » (deuxième vertical slice, P2B-2D). Orchestre la
/// construction de la commande et de ses articles puis la persiste, en remplacement de l'orchestration métier
/// auparavant portée par la branche création de <c>OrderFormViewModel.SaveAsync</c>.
/// </summary>
/// <remarks>
/// Périmètre P2B-2D : <b>création</b> uniquement. La modification (édition d'une commande existante), le
/// changement de statut, la réception et l'encaissement restent dans les ViewModels et seront migrés
/// ultérieurement (strangler, un flux à la fois).
/// </remarks>
public interface ICreateOrderUseCase
{
    /// <summary>
    /// Exécute la création de la commande décrite par <paramref name="command"/> et renvoie le résultat
    /// (identifiant, numéro, statut, date estimée). Les erreurs techniques sont propagées telles quelles
    /// (notamment <see cref="MMV.Domain.Exceptions.PersistenceException"/>), exactement comme le flux d'origine.
    /// </summary>
    Task<CreateOrderResult> ExecuteAsync(CreateOrderCommand command, CancellationToken cancellationToken = default);
}
