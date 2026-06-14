using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Stock.CreateStockMovement;

/// <summary>
/// Cas d'utilisation « Créer un mouvement manuel de stock » (quatrième vertical slice, P2B-2F). Orchestre, de
/// façon atomique, la création d'<b>un</b> mouvement de stock et l'application de son effet sur le produit
/// (incrément, décrément sûr, ou ajustement absolu), en remplacement de l'orchestration métier auparavant portée
/// par <c>StockMovementFormViewModel.SaveAsync</c>.
/// </summary>
/// <remarks>
/// Périmètre P2B-2F : <b>création d'un mouvement manuel</b> uniquement. Le mouvement automatique de vente, le
/// mouvement de fabrication de commande, l'historique, l'édition / suppression de produit, les alertes de stock
/// et l'inventaire complet restent hors périmètre et seront migrés ultérieurement (strangler, un flux à la fois).
/// La ViewModel conserve l'orchestration multi-lignes (boucle sur le formulaire multi-produits, comptage des
/// succès / erreurs, message de synthèse) et appelle ce use case une fois par ligne valide.
/// </remarks>
public interface ICreateStockMovementUseCase
{
    /// <summary>
    /// Exécute la création du mouvement manuel décrit par <paramref name="command"/> dans une frontière
    /// transactionnelle (tout ou rien) et renvoie le résultat (produit retrouvé ou non, mouvement créé, nouveau
    /// stock). Les refus métier (stock insuffisant) et erreurs techniques sont propagés sous forme d'exceptions
    /// (<see cref="MMV.Domain.Exceptions.InsufficientStockException"/>,
    /// <see cref="MMV.Domain.Exceptions.PersistenceException"/>), exactement comme le flux d'origine.
    /// </summary>
    Task<CreateStockMovementResult> ExecuteAsync(CreateStockMovementCommand command, CancellationToken cancellationToken = default);
}
