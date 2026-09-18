using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des commandes.
/// </summary>
public interface IOrderRepository : IGenericRepository<Order, long>
{
    /// <summary>
    /// Récupère toutes les commandes avec leurs articles (OrderItems).
    /// </summary>
    Task<IList<Order>> GetAllWithItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère une commande avec tous ses articles.
    /// </summary>
    Task<Order?> GetWithItemsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche une commande par son numéro.
    /// </summary>
    Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes d'une vente spécifique.
    /// </summary>
    Task<IList<Order>> GetBySaleIdAsync(long saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prise de statut <b>atomique conditionnelle</b> (P3-5) : fait passer la commande <paramref name="orderId"/>
    /// de <paramref name="expectedStatus"/> à <paramref name="nextStatus"/> <b>uniquement si</b> le statut réellement
    /// stocké est encore <paramref name="expectedStatus"/>. Réalisée par une seule instruction
    /// <c>UPDATE … WHERE OrderId = @id AND Status = @expected</c> (aucune comparaison en mémoire), afin que deux
    /// postes tentant simultanément la même transition n'obtiennent qu'<b>une seule</b> réussite.
    /// </summary>
    /// <returns><c>true</c> si la transition a été prise (1 ligne affectée) ; <c>false</c> si le statut stocké ne
    /// correspondait plus (0 ligne : commande déjà avancée, rejouée, ou introuvable).</returns>
    Task<bool> TryTransitionStatusAsync(long orderId, OrderStatus expectedStatus, OrderStatus nextStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Prise de statut <b>atomique conditionnelle liée à la fiche atelier</b> (P3-6B) : fait passer la commande de
    /// <paramref name="expectedStatus"/> à <paramref name="nextStatus"/> <b>uniquement si</b>, au moment exact de
    /// l'écriture, le statut stocké est encore <paramref name="expectedStatus"/> <b>et</b> que l'exigence de fiche
    /// atelier est toujours satisfaite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi une primitive dédiée.</b> Vérifier le contrôle qualité par une lecture, puis prendre le statut
    /// par un <c>UPDATE</c> ne portant que sur <c>Status</c>, laisse une fenêtre réelle : entre les deux, un autre
    /// poste peut régénérer la fiche, rendant courante une version <c>Pending</c>. La commande passerait alors
    /// « Prête » sur la foi d'un contrôle qualité qui ne porte plus sur la version autoritaire. Les deux
    /// conditions doivent donc être évaluées par la <b>même</b> instruction que l'écriture.
    /// </para>
    /// <para>
    /// <b>Exigence de fiche.</b> Lorsque <paramref name="expectedWorkshopSheetId"/> vaut <c>null</c>, l'appelant a
    /// constaté une commande <b>sans aucune fiche</b> (commande historique antérieure à P3-6B) : la transition
    /// n'est alors autorisée que s'il n'existe <b>toujours</b> aucune fiche pour cette commande. Une fiche créée
    /// entre-temps invalide l'autorisation — on ne rend jamais une commande « Prête » sur la base d'une ancienne
    /// lecture « aucune fiche ». Sinon, la fiche désignée doit encore appartenir à cette commande, être la version
    /// <b>courante</b>, porter un contrôle qualité <b>validé</b>, et présenter l'empreinte technique attendue.
    /// </para>
    /// </remarks>
    /// <param name="expectedWorkshopSheetId">Identifiant de la fiche vérifiée, ou <c>null</c> si aucune n'existait.</param>
    /// <param name="expectedFingerprint">Empreinte technique attendue de cette fiche (ignorée si <c>null</c>).</param>
    /// <returns>
    /// <see cref="OrderReadyTransitionOutcome.Taken"/> si la transition a été prise ;
    /// <see cref="OrderReadyTransitionOutcome.StatusConflict"/> si le statut stocké ne correspondait plus ;
    /// <see cref="OrderReadyTransitionOutcome.WorkshopSheetRequirementNotMet"/> si l'exigence de fiche n'était
    /// plus satisfaite. Dans les deux cas d'échec, aucune écriture n'a eu lieu.
    /// </returns>
    Task<OrderReadyTransitionOutcome> TryTransitionWithWorkshopSheetAsync(
        long orderId,
        OrderStatus expectedStatus,
        OrderStatus nextStatus,
        long? expectedWorkshopSheetId,
        string? expectedFingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes par statut.
    /// </summary>
    Task<IList<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes créées entre deux dates.
    /// </summary>
    Task<IList<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les commandes en retard : livraison estimée dépassée à l'instant
    /// <paramref name="asOfUtc"/>, et non encore livrées.
    /// </summary>
    /// <param name="asOfUtc">
    /// Instant de référence, <b>fourni par l'appelant</b> et portant <see cref="DateTimeKind.Utc"/>.
    ///
    /// <para>
    /// P4-5D : cette méthode lisait <c>DateTime.Now</c> pour son propre compte. C'était le deuxième site
    /// prioritaire d'ADR-PROD-DB-004 §5 (décision 3b), et le plus insidieux : la comparaison est
    /// <b>traduite en SQL</b>, donc un instant <see cref="DateTimeKind.Local"/> s'y trouvait confronté à
    /// une colonne <c>timestamptz</c> — décalé d'une à deux heures selon la saison, sans aucune erreur.
    /// </para>
    ///
    /// <para>
    /// Le repository ne reçoit pas <c>IClock</c> : il est instancié directement par <c>UnitOfWork</c> et
    /// par plus de cinquante sites de test. Rendre l'instant <b>explicite dans la signature</b> est à la
    /// fois le plus petit changement et le plus honnête — un repository ne décide pas de l'instant
    /// courant, son appelant le lui dit, et le test peut dès lors le fixer.
    /// </para>
    /// </param>
    Task<IList<Order>> GetOverdueOrdersAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);

    // =============================================================================================================
    // Fiche atelier (P3-6B) — agrégat strictement possédé par la commande.
    //
    // Ces opérations vivent sur le contrat Commande — et non sur un repository dédié — parce que la fiche est un
    // agrégat *dépendant* : elle n'est jamais créée, lue ni versionnée autrement que par l'identifiant de sa
    // commande, elle n'est jamais supprimée, et son cycle de vie est piloté par le workflow de la commande.
    // =============================================================================================================

    /// <summary>
    /// Récupère la <b>version courante</b> de la fiche atelier d'une commande, ou <c>null</c> si la commande n'en
    /// possède aucune (commande historique antérieure à P3-6B, ou fabrication non commencée).
    /// </summary>
    /// <param name="includeItems">Si <c>true</c>, charge aussi les lignes snapshot (ordonnées par position).</param>
    Task<WorkshopSheet?> GetCurrentWorkshopSheetAsync(long orderId, bool includeItems = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère une version précise de fiche atelier par son identifiant, ou <c>null</c> si elle n'existe pas.
    /// </summary>
    Task<WorkshopSheet?> GetWorkshopSheetAsync(long workshopSheetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère toutes les versions de fiche atelier d'une commande, <b>version décroissante</b> (la plus récente
    /// d'abord), lignes snapshot incluses.
    /// </summary>
    Task<IList<WorkshopSheet>> GetWorkshopSheetVersionsAsync(long orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crée la <b>version suivante</b> de la fiche atelier de <paramref name="order"/>, à partir de la version
    /// courante que l'appelant a lue (<paramref name="precondition"/>) : bascule cette version-là hors de l'état
    /// « courante », puis insère la version <c>précondition.NextVersion</c> (<c>IsCurrent = true</c>, contrôle
    /// qualité <c>Pending</c>) — le tout dans la transaction en cours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Compare-and-swap, pas <c>MAX + 1</c>.</b> La bascule est une écriture conditionnelle sur la version
    /// <b>attendue</b> : elle n'affecte une ligne que si cette version est encore la version courante de cette
    /// commande. Deux postes ayant tous deux lu la version 1 obtiennent donc exactement une v2 et un
    /// <see cref="MMV.Domain.Exceptions.WorkshopSheetVersionConflictException"/> — jamais une v3 créée
    /// silencieusement par-dessus une régénération qu'ils n'ont pas vue.
    /// </para>
    /// <para>
    /// <b>Première version.</b> Avec <see cref="WorkshopSheetVersionPrecondition.None"/>, aucune bascule n'a lieu
    /// et la version 1 est insérée : ce sont les contraintes d'unicité <c>(OrderId, Version)</c> et de version
    /// courante unique qui arbitrent une création concurrente, en conflit contrôlé.
    /// </para>
    /// <para>
    /// Aucun message EF/SQLite brut ne franchit l'Infrastructure, et <b>aucune re-tentative</b> n'est effectuée
    /// ici : après une violation de contrainte, la transaction n'est plus utilisable. La transaction englobante
    /// étant annulée, la désactivation de l'ancienne version courante l'est aussi — jamais de commande sans
    /// version courante. L'appelant relance explicitement l'opération s'il le souhaite.
    /// </para>
    /// </remarks>
    /// <param name="order">Commande source, avec ses lignes (et leurs produits si disponibles).</param>
    /// <param name="precondition">Version courante lue par l'appelant, qui doit encore l'être à la bascule.</param>
    /// <param name="createdAt">Horodatage de création de la version.</param>
    /// <returns>La version nouvellement créée.</returns>
    Task<WorkshopSheet> CreateNextWorkshopSheetVersionAsync(
        Order order,
        WorkshopSheetVersionPrecondition precondition,
        DateTime createdAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prend <b>atomiquement</b> la décision de contrôle qualité <paramref name="decision"/> sur la fiche
    /// <paramref name="workshopSheetId"/> : un unique <c>UPDATE … WHERE WorkshopSheetId = @id AND QcStatus =
    /// 'Pending' AND IsCurrent = 1</c>.
    /// </summary>
    /// <remarks>
    /// Aucune comparaison en mémoire : deux postes décidant simultanément n'obtiennent qu'<b>une seule</b>
    /// réussite, et une version devenue non courante entre-temps ne peut plus être décidée.
    /// </remarks>
    /// <returns>
    /// <c>true</c> si la décision a été prise (1 ligne affectée) ; <c>false</c> si la fiche est introuvable, n'est
    /// plus courante, ou porte déjà une décision.
    /// </returns>
    Task<bool> TryTakeWorkshopSheetQcDecisionAsync(
        long workshopSheetId,
        WorkshopSheetQcStatus decision,
        string? comment,
        DateTime completedAt,
        CancellationToken cancellationToken = default);
}
