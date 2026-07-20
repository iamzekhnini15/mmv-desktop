using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des ventes.
/// </summary>
public interface ISaleRepository : IGenericRepository<Sale, long>
{
    /// <summary>
    /// Récupère toutes les ventes avec leurs articles (SaleItems).
    /// </summary>
    Task<IList<Sale>> GetAllWithItemsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère une vente avec tous ses articles.
    /// </summary>
    Task<Sale?> GetWithItemsAsync(long saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche une vente par son numéro.
    /// </summary>
    Task<Sale?> GetBySaleNumberAsync(string saleNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ventes d'un client.
    /// </summary>
    Task<IList<Sale>> GetByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indique si le client possède au moins une vente (P3-2B). Requête d'existence légère : aucune vente
    /// n'est matérialisée, contrairement à <see cref="GetByCustomerIdAsync"/>.
    /// </summary>
    Task<bool> ExistsByCustomerIdAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ventes créées entre deux dates.
    /// </summary>
    Task<IList<Sale>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calcule le total des ventes pour une période.
    /// </summary>
    Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les ventes par méthode de paiement.
    /// </summary>
    Task<IList<Sale>> GetByPaymentMethodAsync(PaymentMethod paymentMethod, CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Règlement atomique conditionnel</b> de l'intégralité du solde restant d'une vente (P3-7) : solde le
    /// solde de la vente <paramref name="saleId"/> <b>uniquement si</b> il reste réellement quelque chose à
    /// encaisser au moment exact de l'écriture. Réalisé par une seule instruction
    /// <c>UPDATE … WHERE SaleId = @id AND RemainingAmount &gt; 0</c>, qui pose <c>DepositAmount = FinalAmount</c>,
    /// <c>RemainingAmount = 0</c> et <c>PaymentStatus = Paid</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi une primitive.</b> Le règlement était, avant P3-7, le <b>seul</b> acte sensible du système
    /// dépourvu de protection concurrentielle — alors que le stock (P2A-1D), la numérotation (P2A-1E) et la fiche
    /// atelier (P3-6B) en disposent tous. Deux postes réglant le même solde réussissaient tous les deux et
    /// créaient <b>deux</b> notifications d'encaissement du même montant. La condition étant désormais évaluée par
    /// l'instruction d'écriture, un seul appel peut faire passer le solde à zéro.
    /// </para>
    /// <para>
    /// <b>Idempotence.</b> Un second appel, ou un rejeu, n'affecte aucune ligne : aucune écriture, aucune
    /// notification. Le montant déjà encaissé n'est jamais réencaissé.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>true</c> si le solde a été réglé par cet appel (1 ligne affectée) ; <c>false</c> si aucune ligne n'a été
    /// affectée (vente introuvable, solde déjà nul, ou règlement concurrent survenu entre-temps). L'appelant
    /// distingue ces cas par une lecture <b>purement diagnostique</b>, qui ne décide jamais de l'écriture.
    /// </returns>
    Task<bool> TrySettleRemainingBalanceAsync(long saleId, CancellationToken cancellationToken = default);
}
