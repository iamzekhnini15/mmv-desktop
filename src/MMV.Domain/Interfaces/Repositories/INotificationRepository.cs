using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des notifications.
///
/// <para>
/// P3-8 — Le contrat expose désormais les <b>primitives ensemblistes</b> nécessaires à la réconciliation des alertes
/// de stock bas. Leur absence était la lacune structurelle qui forçait la couche Application à recharger toute la
/// table <c>Notifications</c> pour chaque produit sous seuil (N+1 en <c>O(N × M)</c>).
/// </para>
///
/// <para>
/// Les méthodes de marquage individuel (<c>MarkAsReadAsync</c>) et de lecture des non lues
/// (<c>GetUnreadNotificationsAsync</c>) ont été <b>supprimées</b> : code mort sans aucun appelant, et
/// <c>MarkAsReadAsync</c> était en outre latent-défectueux (il déléguait à un <c>UpdateAsync</c> sans
/// <c>SaveChangesAsync</c>, donc ne persistait rien hors d'un use case qui commit).
/// </para>
/// </summary>
public interface INotificationRepository : IGenericRepository<Notification, long>
{
    /// <summary>
    /// Marque toutes les notifications comme lues. Écriture <b>ensembliste</b>, atomique et idempotente ; n'affecte
    /// <b>jamais</b> <c>Notification.ResolvedAt</c> — lire n'est pas résoudre.
    /// </summary>
    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Compte les notifications <b>non lues et non résolues</b> (P3-8).
    ///
    /// <para>
    /// L'exclusion des résolues répare un compteur durablement faux : avant P3-8, une alerte portant sur un produit
    /// réapprovisionné depuis longtemps restait non lue et comptée indéfiniment. Les faits historiques
    /// (transition, encaissement, information) ne sont jamais résolus et restent donc comptés normalement tant
    /// qu'ils ne sont pas lus.
    /// </para>
    /// </summary>
    Task<int> CountUnreadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Renvoie les <c>EntityId</c> <b>distincts</b> des alertes de stock bas <b>encore actives</b> (P3-8) —
    /// c'est-à-dire <c>Type = LowStock</c>, <c>EntityType = Product</c>, <c>EntityId</c> non nul et
    /// <c>ResolvedAt IS NULL</c>.
    ///
    /// <para>
    /// Filtrage et projection sont poussés en SQL : aucun titre, aucun message, aucune entité complète n'est
    /// matérialisé. Une seule exécution suffit à trancher le sort de tous les produits, quel qu'en soit le nombre.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<long>> GetActiveLowStockEntityIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Résolution ensembliste</b> des alertes de stock bas actives portant sur les produits fournis (P3-8) : une
    /// seule mise à jour conditionnelle (<c>ResolvedAt IS NULL</c>) pour l'ensemble du lot.
    /// </summary>
    /// <param name="productIds">Produits dont l'alerte doit être fermée. Collection vide ⇒ aucune requête émise.</param>
    /// <param name="resolvedAt">Horodatage de résolution.</param>
    /// <returns>Nombre de lignes réellement résolues (0 si la collection est vide ou si tout était déjà résolu).</returns>
    Task<int> ResolveActiveLowStockAsync(
        IReadOnlyCollection<long> productIds,
        DateTime resolvedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Création atomique</b> d'une alerte de stock bas active (P3-8) : l'existence et l'insertion sont évaluées
    /// par la <b>même</b> instruction, ce qui ferme la course multi-poste qu'un <c>AnyAsync</c> suivi d'un
    /// <c>Add</c> laisse ouverte.
    ///
    /// <para>
    /// Le conflit est ignoré <b>uniquement</b> sur violation d'unicité (l'autre poste a gagné la course) : c'est un
    /// refus métier silencieux, pas une erreur. Toute autre violation de persistance continue de produire
    /// l'exception Infrastructure normale, et aucun message SQLite ou EF ne remonte à la couche Application.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> si la ligne a été créée ; <c>false</c> si une alerte active existait déjà pour cette clé.</returns>
    Task<bool> TryCreateActiveLowStockAsync(Notification notification, CancellationToken cancellationToken = default);
}
