using System;

namespace MMV.Application.UseCases.Notifications.ListNotifications;

/// <summary>
/// DTO applicatif (lecture seule) d'une notification affichée (P2D-3). Porte les champs consommés par
/// <c>NotificationsListViewModel</c> et son écran (<c>NotificationsView</c>).
/// </summary>
public sealed class NotificationListItemDto
{
    /// <summary>Identifiant de la notification.</summary>
    public long NotificationId { get; init; }

    /// <summary>Type technique (LowStock, StockOut, …).</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Titre affiché.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Message affiché.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Indique si la notification est lue.</summary>
    public bool IsRead { get; init; }

    /// <summary>Date de création (utilisée pour le tri décroissant à l'affichage).</summary>
    public DateTime CreatedAt { get; init; }
}
