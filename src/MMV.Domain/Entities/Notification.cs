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
    /// Type de notification.
    /// </summary>
    public string Type { get; set; } = string.Empty; // "LowStock", "StockOut", "OrderReceived", etc.

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
}
