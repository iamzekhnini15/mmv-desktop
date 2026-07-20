using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des clients.
/// </summary>
public interface ICustomerRepository : IGenericRepository<Customer, long>
{
    /// <summary>
    /// Récupère les clients, en excluant les archivés sauf si <paramref name="includeArchived"/> est
    /// <c>true</c> (P3-2B). Le filtrage est poussé dans la requête SQL : aucun client archivé n'est
    /// matérialisé quand il est exclu.
    /// </summary>
    Task<IList<Customer>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche des clients par nom (partiel).
    /// </summary>
    Task<IList<Customer>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche un client par numéro de téléphone.
    /// </summary>
    Task<Customer?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche un client par email.
    /// </summary>
    Task<Customer?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère un client avec toutes ses prescriptions.
    /// </summary>
    Task<Customer?> GetWithPrescriptionsAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère un client avec tout son historique (commandes et ventes).
    /// </summary>
    Task<Customer?> GetWithHistoryAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère tous les clients créés entre deux dates.
    /// </summary>
    Task<IList<Customer>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Prise atomique conditionnelle</b> d'un client <b>actif</b> (P3-7) : réussit uniquement si le client
    /// <paramref name="customerId"/> existe <b>et</b> n'est <b>pas archivé</b> au moment exact de l'écriture, et
    /// maintient la ligne prise jusqu'au commit de la transaction englobante.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi une primitive plutôt qu'une lecture.</b> Charger le client puis tester <c>IsArchived</c> en
    /// mémoire (patron P3-3B) laisse une fenêtre réelle en multi-poste : le poste A peut archiver le client entre
    /// la lecture du poste B et son commit, et la vente passerait malgré la garde — course explicitement assumée
    /// et documentée par P3-3B. Ici la condition est évaluée par la <b>même instruction</b> que l'écriture, dans
    /// la transaction de la vente : il n'existe plus d'intervalle entre décider et écrire.
    /// </para>
    /// <para>
    /// <b>Écriture sans changement fonctionnel.</b> La prise s'exprime comme une mise à jour conditionnelle
    /// réaffectant à <c>IsArchived</c> la valeur qu'il doit déjà avoir (<c>false</c>) : aucune donnée métier n'est
    /// modifiée, seule la ligne est verrouillée jusqu'au commit — même patron que les prises de statut
    /// <c>TryTransitionStatusAsync</c> (P3-5) et le décrément conditionnel de stock (P2A-1D).
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>true</c> si le client actif a été pris (1 ligne affectée) ; <c>false</c> si aucune ligne n'a été prise
    /// (client introuvable <b>ou</b> archivé). L'appelant distingue les deux cas par une lecture
    /// <b>purement diagnostique</b>, qui ne décide jamais de l'écriture.
    /// </returns>
    Task<bool> TryAcquireActiveAsync(long customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lecture <b>fraîche et non suivie</b> d'un client par identifiant (P3-7, revue avant commit) : contourne le
    /// change tracker, donc ne peut jamais renvoyer une entité déjà suivie et périmée dans le <c>DbContext</c> de
    /// la portée. À utiliser pour toute décision (ou tout message) qui doit refléter l'état réel de la base après
    /// une <see cref="TryAcquireActiveAsync"/> — jamais <c>GetByIdAsync</c> (qui peut renvoyer une instance suivie
    /// depuis le cache local du contexte sans requêter la base, via <c>FindAsync</c>).
    /// </summary>
    Task<Customer?> GetByIdFreshAsync(long customerId, CancellationToken cancellationToken = default);
}
