using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Application.UseCases.WorkshopSheets;

namespace MMV.Application.UseCases.Orders.AdvanceOrderStatus;

/// <summary>
/// Use case « Faire avancer le statut / réception d'une commande ». En P3-5, le flux de <b>fabrication</b>
/// (<c>ToFabricate → InProgress</c>) devient <b>sûr et idempotent</b> en multi-poste : décrément atomique via
/// <see cref="IStockMutationService"/> (jamais négatif), prise de statut atomique conditionnelle
/// (<see cref="IOrderRepository.TryTransitionStatusAsync"/>) et frontière transactionnelle unique
/// (<see cref="ITransactionRunner"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Transaction.</b> Tout le flux (prise atomique du statut, décréments, mouvements, notification, sauvegarde)
/// s'exécute dans un unique <see cref="ITransactionRunner"/>. Une erreur de stock
/// (<see cref="InsufficientStockException"/>) ou un conflit de statut (<see cref="OrderStatusConflictException"/>)
/// annule <b>tout</b> : statut non avancé, aucun décrément, aucun mouvement, aucune notification.
/// </para>
/// <para>
/// <b>Idempotence du décrément (garde P3-5, pas la matrice P3-6).</b> Le statut est pris par un
/// <c>UPDATE … WHERE Status = statut attendu</c> (aucune comparaison en mémoire) : deux postes tentant simultanément
/// <c>ToFabricate → InProgress</c>, ou une répétition de la même transition, ne produisent qu'<b>un seul</b> ensemble
/// de décréments et de mouvements ; la seconde tentative est refusée par <see cref="OrderStatusConflictException"/>.
/// </para>
/// <para>
/// <b>Légalité de la transition (matrice P3-6).</b> Avant d'ouvrir la transaction, le couple
/// <c>CurrentStatus → NextStatus</c> est validé par <c>OrderStatusPolicy.IsAllowed</c> (source de vérité unique du
/// Domain). Un couple interdit (saut d'étape, retour arrière, même statut, sortie de
/// <see cref="OrderStatus.Delivered"/>) lève <see cref="InvalidOrderStatusTransitionException"/> <b>sans</b> aucune
/// écriture. Légalité (matrice) et conflit de concurrence (prise atomique) sont deux gardes complémentaires.
/// </para>
/// <para>
/// <see cref="INotificationRepository"/> reste <b>optionnel</b> (peut être <c>null</c>), reproduisant la garde
/// <c>if (_notificationRepository != null)</c> du flux d'origine.
/// </para>
/// </remarks>
public sealed class AdvanceOrderStatusUseCase : IAdvanceOrderStatusUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly IStockMutationService _stockMutationService;
    private readonly INotificationRepository? _notificationRepository;

    public AdvanceOrderStatusUseCase(
        IOrderRepository orderRepository,
        IStockMovementRepository stockMovementRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        IStockMutationService stockMutationService,
        INotificationRepository? notificationRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Frontière transactionnelle obligatoire (P2A-1C, R-23) : statut + décréments + mouvements = tout ou rien.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        // Décrément de stock sûr obligatoire (P2A-1D, R-09) : la fabrication ne peut plus rendre le stock négatif.
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
        // Notification optionnelle (comme le flux d'origine) : si null, aucune notification n'est créée.
        _notificationRepository = notificationRepository;
    }

    /// <inheritdoc />
    public async Task<AdvanceOrderStatusResult> ExecuteAsync(AdvanceOrderStatusCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var previousStatus = command.CurrentStatus;
        var nextStatus = command.NextStatus;

        // Garde de LÉGALITÉ métier (P3-6) : la matrice unique du Domain valide le couple AVANT toute transaction.
        // Un couple interdit (saut d'étape, retour arrière, même statut, sortie de Delivered) est refusé ici même :
        // aucune transaction n'est ouverte ⇒ aucune prise de statut, aucun décrément, aucun mouvement, aucune
        // notification. La prise atomique conditionnelle (P3-5) reste ensuite responsable du CONFLIT de concurrence.
        if (!OrderStatusPolicy.IsAllowed(previousStatus, nextStatus))
        {
            throw new InvalidOrderStatusTransitionException(previousStatus, nextStatus);
        }

        return await _transactionRunner.RunAsync(async token =>
        {
            // Recharger la commande fraîche (articles + produits) pour construire les mouvements de fabrication.
            var fresh = await _orderRepository.GetWithItemsAsync(command.OrderId, token);
            if (fresh is null)
            {
                // « Commande introuvable. » côté ViewModel : aucun changement d'état, aucune écriture.
                return new AdvanceOrderStatusResult { OrderFound = false };
            }

            // Garde de CLASSIFICATION (P3-11), opposée au seul passage qui consomme réellement du stock. Elle
            // s'exécute AVANT la prise atomique du statut : un refus laisse donc le statut, le stock, les
            // mouvements, la fiche et les notifications strictement inchangés.
            if (previousStatus == OrderStatus.ToFabricate && nextStatus == OrderStatus.InProgress)
            {
                RequireConsistentStockFlow(fresh);
            }

            // Prise ATOMIQUE du statut : ne réussit que si le statut stocké est encore celui attendu. Empêche le
            // double décrément (concurrence / répétition) sans implémenter la matrice de transitions (P3-6).
            //
            // Le passage QualityCheck → Ready emprunte une prise ENRICHIE (P3-6B) : la même instruction vérifie
            // aussi que l'exigence de fiche atelier tient toujours. La garde métier ci-dessous établit le message
            // précis et l'identité de la fiche vérifiée ; elle ne décide jamais seule de la transition.
            if (previousStatus == OrderStatus.QualityCheck && nextStatus == OrderStatus.Ready)
            {
                var expected = await GetExpectedWorkshopSheetForReadyAsync(fresh, token);

                var outcome = await _orderRepository.TryTransitionWithWorkshopSheetAsync(
                    command.OrderId,
                    previousStatus,
                    nextStatus,
                    expected.WorkshopSheetId,
                    expected.Fingerprint,
                    token);

                if (outcome == OrderReadyTransitionOutcome.StatusConflict)
                {
                    throw new OrderStatusConflictException(command.OrderId, previousStatus);
                }

                if (outcome == OrderReadyTransitionOutcome.WorkshopSheetRequirementNotMet)
                {
                    // Le statut était bon : c'est l'exigence de fiche qui a cédé entre la garde métier et
                    // l'écriture — une autre fiche est devenue courante, ou une fiche est apparue sur une
                    // commande jusque-là dépourvue. Refus métier, aucune écriture, aucun détail technique.
                    throw new BusinessRuleException(WorkshopSheetPolicy.ReadyRequiresUnchangedCurrentSheetMessage);
                }
            }
            else
            {
                var transitionTaken = await _orderRepository.TryTransitionStatusAsync(command.OrderId, previousStatus, nextStatus, token);
                if (!transitionTaken)
                {
                    throw new OrderStatusConflictException(command.OrderId, previousStatus);
                }
            }

            // Génération AUTOMATIQUE de la première fiche atelier (P3-6B), dans la même transaction que la
            // transition. Deux points d'entrée couvrent l'ensemble des commandes :
            //   • ToFabricate → InProgress : le cas nominal — toute commande neuve obtient sa fiche en entrant en
            //     fabrication, donc AVANT de pouvoir atteindre QualityCheck ;
            //   • InProgress → QualityCheck : filet de compatibilité — une commande déjà InProgress au moment du
            //     déploiement de P3-6B n'est jamais passée par le point précédent et obtient sa fiche ici.
            // L'opération est idempotente : une commande possédant déjà une fiche courante n'en crée pas de
            // seconde. Un échec de génération lève et annule TOUT (statut, stock, mouvements, notification).
            var hasCreatedWorkshopSheet = false;
            if ((previousStatus == OrderStatus.ToFabricate && nextStatus == OrderStatus.InProgress)
                || (previousStatus == OrderStatus.InProgress && nextStatus == OrderStatus.QualityCheck))
            {
                var (_, wasCreated) = await WorkshopSheetOperations.EnsureCurrentSheetAsync(
                    _orderRepository, fresh, DateTime.UtcNow, token);
                hasCreatedWorkshopSheet = wasCreated;
            }

            // Sortie de stock lors du passage en fabrication (À fabriquer → En fabrication), désormais via le
            // décrément atomique sûr (jamais négatif). Stock insuffisant ⇒ InsufficientStockException ⇒ rollback.
            var stockMovementCount = 0;
            if (previousStatus == OrderStatus.ToFabricate && nextStatus == OrderStatus.InProgress)
            {
                stockMovementCount = await CreateStockMovementsForFabricationAsync(fresh, token);
            }

            // Créer une notification (si le repository est disponible), texte préservé au caractère près.
            var hasNotification = false;
            if (_notificationRepository != null)
            {
                var notification = new Notification
                {
                    Type = NotificationTypes.OrderStatusChanged,
                    Title = $"Commande {fresh.OrderNumber} : {command.NextStatusDisplay}",
                    Message = $"La commande {fresh.OrderNumber} ({command.CustomerDisplayName}) est passée de " +
                              $"'{command.CurrentStatusDisplay}' à " +
                              $"'{command.NextStatusDisplay}'.",
                    EntityId = fresh.OrderId,
                    EntityType = NotificationEntityTypes.Order,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };
                await _notificationRepository.CreateAsync(notification, token);
                hasNotification = true;
            }

            // Écriture des mouvements + notification. Le statut a déjà été pris atomiquement ci-dessus ; le décrément
            // du stock est appliqué directement en base par le service. Tout est dans la même transaction (runner).
            await _unitOfWork.SaveChangesAsync(token);

            // Refléter le nouveau statut dans l'entité renvoyée à la ViewModel (post-sauvegarde : non re-persisté).
            fresh.Status = nextStatus;

            return new AdvanceOrderStatusResult
            {
                OrderFound = true,
                Order = fresh,
                OldStatus = previousStatus,
                NewStatus = nextStatus,
                HasCreatedStockMovements = stockMovementCount > 0,
                CreatedStockMovementCount = stockMovementCount,
                HasNotification = hasNotification,
                HasCreatedWorkshopSheet = hasCreatedWorkshopSheet
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Garde de contrôle qualité (P3-6B) du passage <c>QualityCheck → Ready</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Compatibilité historique assumée.</b> Une commande <b>sans aucune fiche</b> est autorisée à avancer :
    /// il s'agit nécessairement d'une commande déjà en contrôle qualité au moment du déploiement de P3-6B, que la
    /// génération automatique n'a jamais pu atteindre. La bloquer reviendrait à immobiliser du travail légitime
    /// en cours, et lui fabriquer une fiche « validée » rétroactivement reviendrait à inventer un contrôle qui
    /// n'a jamais eu lieu. Toute commande créée <i>après</i> P3-6B possède une fiche (générée à l'entrée en
    /// fabrication) et est donc bien soumise à la garde.
    /// </para>
    /// <para>
    /// Lorsqu'une fiche existe, deux conditions sont exigées : le contrôle doit être <b>validé</b>, et la fiche
    /// doit toujours <b>correspondre</b> aux données techniques de la commande (une fiche obsolète atteste d'un
    /// contrôle portant sur d'autres données).
    /// </para>
    /// <para>
    /// <b>Cette garde ne suffit pas et ne prétend pas suffire.</b> Elle produit un message métier précis pour les
    /// refus non concurrents, et renvoie l'<b>identité</b> de la fiche vérifiée. C'est la prise atomique
    /// (<see cref="IOrderRepository.TryTransitionWithWorkshopSheetAsync"/>) qui garantit ensuite que cette fiche
    /// est toujours la fiche autoritaire au moment exact de l'écriture — y compris le cas « aucune fiche », qui
    /// est revérifié atomiquement et non tenu pour acquis d'après cette lecture.
    /// </para>
    /// </remarks>
    /// <returns>L'identité de la fiche à exiger lors de la prise atomique (<c>null</c> si aucune n'existe).</returns>
    private async Task<(long? WorkshopSheetId, string? Fingerprint)> GetExpectedWorkshopSheetForReadyAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var sheet = await _orderRepository.GetCurrentWorkshopSheetAsync(order.OrderId, includeItems: false, cancellationToken);

        if (sheet is null)
        {
            // Commande historique sans fiche : compatibilité, aucun faux contrôle qualité n'est fabriqué.
            // L'absence sera reconfirmée atomiquement (NOT EXISTS) au moment de la prise de statut.
            return (null, null);
        }

        if (sheet.QcStatus != WorkshopSheetQcStatus.Passed)
        {
            throw new BusinessRuleException(WorkshopSheetPolicy.ReadyRequiresPassedQcMessage);
        }

        if (!WorkshopSheetOperations.IsUpToDate(sheet, order))
        {
            throw new BusinessRuleException(WorkshopSheetPolicy.ReadyRequiresUpToDateSheetMessage);
        }

        return (sheet.WorkshopSheetId, sheet.TechnicalFingerprint);
    }

    /// <summary>
    /// Garde de classification du flux de stock (P3-11) opposée à l'entrée en fabrication : chaque article dont le
    /// produit est <b>réellement chargé</b> doit être classé comme lui.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi ici alors que la vente est déjà gardée.</b> La garde de
    /// <c>RegisterSaleUseCase</c> protège les ventes enregistrées <b>après</b> P3-11. Elle ne dit rien des
    /// commandes déjà persistées — historiques, créées manuellement par un autre chemin d'écriture, ou altérées en
    /// base. Sans cette seconde garde, une telle commande produirait encore le double décrément que P3-11 a
    /// précisément découvert. Elle protège la <b>donnée</b>, pas seulement le flux nominal.
    /// </para>
    /// <para>
    /// <b>Aucune politique inventée sur les articles sans produit.</b> Un article dont <c>ProductId</c> est nul, ou
    /// dont le produit n'est pas chargé, est laissé <b>exactement</b> comme avant : il n'était déjà décrémenté par
    /// personne (<see cref="CreateStockMovementsForFabricationAsync"/> ne retient que les articles porteurs d'un
    /// <c>ProductId</c>), et P3-11 n'a pas mandat pour trancher son sort.
    /// </para>
    /// </remarks>
    /// <exception cref="BusinessRuleException">si un article contredit la catégorie de son produit.</exception>
    private static void RequireConsistentStockFlow(Order order)
    {
        foreach (var item in order.OrderItems)
        {
            if (item.Product is null)
                continue;

            if (!SaleLineStockFlowPolicy.IsCompatible(item.ItemType, item.Product.Category))
                throw new BusinessRuleException(SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage);
        }
    }

    /// <summary>
    /// Décrémente le stock de chaque article lié à un produit via le décrément atomique sûr
    /// (<see cref="IStockMutationService.DecrementStockAsync"/>) et crée le mouvement <c>Out</c> correspondant
    /// (<c>Quantity</c> <b>négative</b>, convention de signe P3-5). Un stock insuffisant lève
    /// <see cref="InsufficientStockException"/> ⇒ rollback complet par le runner (statut non avancé).
    /// </summary>
    private async Task<int> CreateStockMovementsForFabricationAsync(Order order, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var item in order.OrderItems.Where(i => i.ProductId.HasValue))
        {
            // Décrément atomique conditionnel : jamais négatif (contrairement à l'ancien `-=` non borné).
            await _stockMutationService.DecrementStockAsync(item.ProductId!.Value, item.Quantity, cancellationToken);

            var movement = new StockMovement
            {
                ProductId = item.ProductId!.Value,
                MovementType = StockMovementType.Out,
                Quantity = -item.Quantity, // sortie ⇒ delta négatif (convention P3-5).
                Reason = $"Fabrication commande {order.OrderNumber}",
                CreatedAt = DateTime.UtcNow,
            };

            await _stockMovementRepository.CreateAsync(movement, cancellationToken);
            count++;
        }

        return count;
    }
}
