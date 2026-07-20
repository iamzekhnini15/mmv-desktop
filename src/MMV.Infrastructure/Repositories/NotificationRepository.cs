using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MMV.Domain.Constants;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des notifications.
/// </summary>
public class NotificationRepository : BaseRepository<Notification, long>, INotificationRepository
{
    public NotificationRepository(OpticDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        // Écriture ensembliste : une seule requête serveur, aucune matérialisation, aucun tracking. Deux postes
        // concurrents convergent vers le même état (la cible est une constante, pas une valeur relue).
        //
        // ResolvedAt n'est délibérément PAS touché : marquer comme lu n'a AUCUN effet sur le cycle de vie métier de
        // l'alerte. C'est précisément la confusion que P3-8 supprime — avant, « tout marquer comme lu » rendait
        // l'anti-doublon aveugle et la génération suivante recréait l'intégralité du jeu d'alertes.
        await _context.Notifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountUnreadAsync(CancellationToken cancellationToken = default)
    {
        // « Non lue ET non résolue » (P3-8) : le badge doit refléter des problèmes ACTIFS. Une alerte portant sur un
        // produit réapprovisionné depuis longtemps ne doit plus être comptée, même si personne ne l'a jamais ouverte.
        // Les faits historiques (transition, encaissement, information) ne sont jamais résolus : pour eux, le
        // prédicat se réduit à l'ancien comportement.
        return await GetQueryable()
            .CountAsync(n => !n.IsRead && n.ResolvedAt == null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetActiveLowStockEntityIdsAsync(CancellationToken cancellationToken = default)
    {
        // Projection minimale : seuls les identifiants traversent la frontière SQL. Le prédicat reprend exactement
        // les colonnes de l'index unique filtré, qui sert donc aussi cette lecture.
        return await GetQueryable()
            .Where(n => n.Type == NotificationTypes.LowStock
                        && n.EntityType == NotificationEntityTypes.Product
                        && n.EntityId != null
                        && n.ResolvedAt == null)
            .Select(n => n.EntityId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> ResolveActiveLowStockAsync(
        IReadOnlyCollection<long> productIds,
        DateTime resolvedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);

        // Lot vide ⇒ aucune requête émise. Le cas est fréquent (régime permanent : rien à résoudre) et ne doit pas
        // coûter un aller-retour.
        if (productIds.Count == 0)
        {
            return 0;
        }

        var ids = productIds.ToArray();

        // Une SEULE mise à jour conditionnelle pour tout le lot. La condition « ResolvedAt IS NULL » rend l'appel
        // idempotent : une seconde réconciliation ne réécrit pas la date de résolution déjà posée.
        return await _context.Notifications
            .Where(n => n.Type == NotificationTypes.LowStock
                        && n.EntityType == NotificationEntityTypes.Product
                        && n.ResolvedAt == null
                        && n.EntityId != null
                        && ids.Contains(n.EntityId.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.ResolvedAt, resolvedAt),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateActiveLowStockAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Garde de contrat : cette primitive est spécialisée. Elle ne doit jamais servir à insérer un fait
        // historique, qui échapperait alors à l'index unique filtré et créerait l'illusion d'une protection.
        if (notification.Type != NotificationTypes.LowStock
            || notification.EntityType != NotificationEntityTypes.Product
            || notification.EntityId is null)
        {
            throw new ArgumentException(
                "TryCreateActiveLowStockAsync n'accepte qu'une alerte LowStock/Product portant un EntityId.",
                nameof(notification));
        }

        // Insertion ATOMIQUE : l'existence et l'écriture sont évaluées par la MÊME instruction. Un AnyAsync suivi
        // d'un Add laisserait une fenêtre entre la lecture et l'écriture, dans laquelle un second poste insère.
        //
        // « ON CONFLICT DO NOTHING » (SQLite ≥ 3.24) et NON « INSERT OR IGNORE » : la forme upsert n'absorbe que les
        // violations d'UNICITÉ. Les violations NOT NULL, CHECK et de clé étrangère continuent de lever, donc de
        // remonter comme erreur Infrastructure normale — alors qu'« OR IGNORE » les masquerait toutes en silence et
        // transformerait un bug d'écriture en ligne manquante indétectable. Aucun catch générique n'est employé.
        //
        // Le conflit visé est l'index unique filtré (Type, EntityType, EntityId) WHERE actif : sans cible explicite,
        // DO NOTHING s'applique à toutes les contraintes d'unicité, index partiels compris.
        const string sql =
            "INSERT INTO \"Notifications\" " +
            "(\"Type\", \"Title\", \"Message\", \"EntityId\", \"EntityType\", \"IsRead\", \"CreatedAt\", \"ResolvedAt\") " +
            "VALUES (@type, @title, @message, @entityId, @entityType, @isRead, @createdAt, NULL) " +
            "ON CONFLICT DO NOTHING;";

        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
            sql,
            new[]
            {
                new SqliteParameter("@type", notification.Type),
                new SqliteParameter("@title", notification.Title),
                new SqliteParameter("@message", notification.Message),
                new SqliteParameter("@entityId", notification.EntityId.Value),
                new SqliteParameter("@entityType", notification.EntityType),
                new SqliteParameter("@isRead", notification.IsRead),
                new SqliteParameter("@createdAt", notification.CreatedAt),
            },
            cancellationToken);

        // 1 ⇒ l'alerte a été ouverte. 0 ⇒ une alerte active existait déjà pour cette clé (un autre poste a gagné la
        // course) : refus métier silencieux, jamais une erreur technique remontée à l'Application.
        return rowsAffected == 1;
    }
}
