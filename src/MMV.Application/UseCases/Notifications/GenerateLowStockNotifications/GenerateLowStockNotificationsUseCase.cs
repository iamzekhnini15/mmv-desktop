using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;

/// <summary>
/// Use case « Réconcilier les alertes de stock bas » (P3-8) — anciennement « Générer les notifications de stock bas ».
///
/// <para>
/// <b>Ce n'est plus un générateur, c'est un réconciliateur.</b> À chaque exécution il rapproche deux ensembles —
/// les produits actuellement sous seuil et les alertes actuellement actives — puis applique la différence dans les
/// deux sens : il <b>ouvre</b> les alertes manquantes et <b>ferme</b> celles devenues fausses. C'est ce second sens
/// qui répare les alertes fantômes (produit réapprovisionné, désactivé, supprimé, ou seuil abaissé) sans exiger le
/// moindre code dédié à chacun de ces cas.
/// </para>
///
/// <para>
/// <b>Trois défauts prouvés de l'implémentation d'origine sont supprimés ici :</b>
/// <list type="number">
///   <item><b>Doublon après lecture (déterministe, mono-poste).</b> L'anti-doublon testait <c>!IsRead</c> : après un
///   « tout marquer comme lu », il devenait aveugle et la connexion suivante recréait une alerte pour CHAQUE produit
///   encore sous seuil. Le prédicat repose désormais sur <c>ResolvedAt IS NULL</c> — l'état métier — et jamais sur
///   l'état de lecture.</item>
///   <item><b>N+1 en <c>O(N × M)</c>.</b> Toute la table <c>Notifications</c> était rechargée à l'intérieur de la
///   boucle produits. Le nombre de LECTURES est maintenant CONSTANT (deux requêtes de réconciliation + un compteur),
///   quel que soit le nombre de produits sous seuil.</item>
///   <item><b>Course multi-poste.</b> Lecture puis écriture non atomiques, hors transaction, sans contrainte de base.
///   L'ouverture passe désormais par une primitive d'insertion atomique adossée à un index unique filtré, et toute
///   l'exécution tient dans un unique <see cref="ITransactionRunner"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Produits inactifs.</b> Ils sont exclus de la population par le port produit lui-même. Conséquence utile et
/// gratuite : leurs alertes actives se retrouvent mécaniquement dans l'ensemble « à résoudre », donc un produit
/// retiré du catalogue voit son alerte se fermer au premier passage au lieu d'être réalertée indéfiniment.
/// </para>
/// </summary>
public sealed class GenerateLowStockNotificationsUseCase : IGenerateLowStockNotificationsUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;

    public GenerateLowStockNotificationsUseCase(
        IProductRepository productRepository,
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<GenerateLowStockNotificationsResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        // Fermeture et ouverture sont tout-ou-rien : une réconciliation partiellement appliquée laisserait la base
        // dans un état qu'aucune exécution ultérieure ne distinguerait d'un état normal.
        return await _transactionRunner.RunAsync(async token =>
        {
            // (1) Population cible — une seule requête, filtrée en SQL (actif ET au niveau ou sous le seuil).
            var lowStockProducts = await _productRepository.GetActiveLowStockAsync(token);
            var lowStockProductIds = new HashSet<long>(lowStockProducts.Select(p => p.ProductId));

            // (2) État courant — une seule requête, projetant les seuls identifiants des alertes ACTIVES.
            var activeAlertIds = new HashSet<long>(await _notificationRepository.GetActiveLowStockEntityIdsAsync(token));

            // (3) Différence symétrique. Deux opérations ensemblistes en mémoire sur des HashSet : O(1) par élément,
            //     donc aucune lecture supplémentaire ne dépend du nombre de produits.
            //     • à résoudre : une alerte est active alors que son produit n'est plus sous seuil (réapprovisionné,
            //       désactivé, supprimé, ou seuil abaissé) ;
            //     • à ouvrir   : un produit est sous seuil sans alerte active.
            var idsToResolve = activeAlertIds.Where(id => !lowStockProductIds.Contains(id)).ToArray();
            var productsToCreate = lowStockProducts.Where(p => !activeAlertIds.Contains(p.ProductId)).ToList();

            // (4) Fermeture : une seule mise à jour ensembliste conditionnelle pour tout le lot.
            var resolved = await _notificationRepository.ResolveActiveLowStockAsync(idsToResolve, DateTime.Now, token);

            // (5) Ouverture : une insertion atomique par alerte réellement manquante. Le volume d'ÉCRITURES croît
            //     légitimement avec le nombre de nouvelles alertes — c'est le coût irréductible d'une création ;
            //     seul le volume de LECTURES devait devenir constant.
            var created = 0;
            foreach (var product in productsToCreate)
            {
                var notification = new Notification
                {
                    Type = NotificationTypes.LowStock,
                    Title = $"Stock bas : {product.Name}",
                    Message = $"Le produit {product.Reference} - {product.Name} est en stock bas ({product.StockQuantity}/{product.StockAlertThreshold})",
                    EntityId = product.ProductId,
                    EntityType = NotificationEntityTypes.Product,
                    IsRead = false,
                    CreatedAt = DateTime.Now,
                };

                // Un « false » signifie qu'un autre poste a ouvert la même alerte entre nos étapes (2) et (5) : ce
                // n'est pas une erreur mais un refus métier attendu, et il ne doit surtout pas être compté comme une
                // création — sinon le résultat annoncerait des alertes qui n'existent pas.
                if (await _notificationRepository.TryCreateActiveLowStockAsync(notification, token))
                {
                    created++;
                }
            }

            // Les trois primitives ci-dessus écrivent directement en base dans la transaction courante ; ce
            // SaveChanges valide toute écriture suivie qui aurait pu être enregistrée dans la même portée et
            // maintient le contrat d'unité de travail du use case.
            await _unitOfWork.SaveChangesAsync(token);

            // (6) Compteur relu APRÈS réconciliation : il reflète l'état réellement commité, pas une projection.
            var unread = await _notificationRepository.CountUnreadAsync(token);

            return new GenerateLowStockNotificationsResult
            {
                CreatedCount = created,
                ResolvedCount = resolved,
                UnreadCount = unread,
            };
        }, cancellationToken);
    }
}
