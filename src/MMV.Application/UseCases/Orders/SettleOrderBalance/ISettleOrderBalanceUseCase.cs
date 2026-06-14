using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Orders.SettleOrderBalance;

/// <summary>
/// Cas d'utilisation « Encaisser le solde restant d'une commande » (cinquième vertical slice, P2B-2G).
/// Orchestre la persistance de l'encaissement du solde (mise à jour du paiement sur la vente liée, notification,
/// enregistrement), en remplacement de l'orchestration métier auparavant portée par
/// <c>OrderDetailViewModel.ExecuteEncashBalanceAsync</c>.
/// </summary>
/// <remarks>
/// Périmètre P2B-2G : <b>encaissement du solde</b> uniquement. Il s'agit d'un déplacement iso-fonctionnel du flux
/// existant — aucun module financier (paiement, facture, devis, <c>Money</c>, TVA, règles pays) n'est introduit.
/// L'avancement de statut (P2B-2E), la checklist contrôle qualité, l'édition, la suppression et la création
/// restent dans les ViewModels / autres use cases.
/// </remarks>
public interface ISettleOrderBalanceUseCase
{
    /// <summary>
    /// Exécute l'encaissement du solde décrit par <paramref name="command"/> et renvoie le résultat (commande
    /// retrouvée ou non, entité rechargée, montants, notification). Les écritures (mise à jour de la vente puis
    /// notification) sont réalisées dans une frontière transactionnelle unique : toute exception annule
    /// l'ensemble (aucune écriture partielle). Les erreurs techniques sont propagées telles quelles (notamment
    /// <see cref="MMV.Domain.Exceptions.PersistenceException"/>).
    /// </summary>
    Task<SettleOrderBalanceResult> ExecuteAsync(SettleOrderBalanceCommand command, CancellationToken cancellationToken = default);
}
