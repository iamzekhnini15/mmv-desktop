using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.UpdateOrder;

/// <summary>
/// Cas d'utilisation « Modifier une commande existante » (septième vertical slice, P2B-2I). Orchestre le
/// chargement de la commande, la mise à jour de ses champs et la reconstruction de ses lignes puis la persiste,
/// en remplacement de l'orchestration métier auparavant portée par la branche édition de
/// <c>OrderFormViewModel.SaveAsync</c> (méthode <c>UpdateExistingOrderAsync</c>).
/// </summary>
/// <remarks>
/// Périmètre P2B-2I : <b>édition</b> uniquement. La création (P2B-2D), l'avancement de statut (P2B-2E),
/// l'encaissement du solde (P2B-2G) et la suppression (P2B-2H) sont déjà migrés ; les autres flux
/// (navigation Kanban, fiche de fabrication, facturation, etc.) restent hors périmètre.
/// </remarks>
public interface IUpdateOrderUseCase
{
    /// <summary>
    /// Exécute la modification de la commande décrite par <paramref name="command"/> et renvoie le résultat
    /// (identifiant, numéro, statut, date estimée). Les erreurs techniques sont propagées telles quelles
    /// (notamment <see cref="MMV.Domain.Exceptions.PersistenceException"/>), exactement comme le flux d'origine.
    /// </summary>
    Task<UpdateOrderResult> ExecuteAsync(UpdateOrderCommand command, CancellationToken cancellationToken = default);
}
