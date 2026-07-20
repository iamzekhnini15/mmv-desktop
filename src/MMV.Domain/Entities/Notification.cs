using System;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente une notification système.
/// </summary>
public class Notification
{
    /// <summary>
    /// Identifiant unique de la notification.
    /// </summary>
    public long NotificationId { get; set; }

    /// <summary>
    /// Type de notification. Valeurs canoniques : voir <c>NotificationTypes</c> — seule source de vérité depuis
    /// P3-8. Le commentaire d'origine énumérait ici des littéraux recopiés, dont un (<c>OrderReceived</c>) qui n'a
    /// jamais existé dans le code : c'est exactement la dérive que la centralisation supprime.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Titre de la notification.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Message de la notification.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Identifiant de l'entité concernée (ProductId, OrderId, etc.).
    /// </summary>
    public long? EntityId { get; set; }

    /// <summary>
    /// Type d'entité concernée (Product, Order, etc.).
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Indique si la notification a été lue.
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// Date de création de la notification.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Date de <b>résolution métier</b> de la notification (P3-8), ou <c>null</c> si la condition qu'elle décrit est
    /// <b>encore active</b>.
    ///
    /// <para>
    /// <b>Axe strictement orthogonal à <see cref="IsRead"/>.</b> <see cref="IsRead"/> dit « l'opérateur a vu le
    /// message » ; <see cref="ResolvedAt"/> dit « la condition métier est terminée ». Les quatre combinaisons sont
    /// légitimes — en particulier « lue mais toujours active » (l'opérateur a vu l'alerte, le stock reste bas) et
    /// « résolue mais jamais consultée » (le stock a été reconstitué avant l'ouverture du panneau). Avant P3-8,
    /// <see cref="IsRead"/> tenait les deux rôles : marquer comme lu rouvrait la porte au doublon alors que la
    /// condition n'avait pas changé.
    /// </para>
    ///
    /// <para>
    /// <b>Portée.</b> Seul <c>NotificationTypes.LowStock</c> sur <c>NotificationEntityTypes.Product</c> possède un
    /// cycle de vie résoluble. Les faits historiques (transition de commande, encaissement, information) restent
    /// <c>null</c> à vie : un paiement reçu ne cesse pas d'être vrai.
    /// </para>
    ///
    /// <para>
    /// <b>Backfill.</b> Nullable sans valeur par défaut : toute notification antérieure vaut <c>null</c>, c'est-à-dire
    /// « active » — exactement ce que l'ancien schéma affirmait (rien n'avait jamais été résolu). Aucun état n'est
    /// inventé.
    /// </para>
    /// </summary>
    public DateTime? ResolvedAt { get; set; }
}
