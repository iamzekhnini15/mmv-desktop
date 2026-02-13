using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente une commande fournisseur (uniquement pour les verres).
/// L'Order est créé automatiquement lorsqu'une Sale contient des verres.
/// </summary>
public class Order
{
    /// <summary>
    /// Identifiant unique de la commande fournisseur.
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// Numéro de commande unique (ex: CMD-2026-0001).
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Identifiant de la vente parente (OBLIGATOIRE).
    /// Une commande fournisseur est toujours liée à une vente.
    /// </summary>
    public long SaleId { get; set; }

    /// <summary>
    /// Identifiant du fournisseur (optionnel pour l'instant).
    /// </summary>
    public long? SupplierId { get; set; }

    /// <summary>
    /// Date et heure de création de la commande.
    /// </summary>
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date estimée de réception des verres.
    /// </summary>
    public DateTime? EstimatedDelivery { get; set; }

    /// <summary>
    /// Date de réception effective des verres.
    /// </summary>
    public DateTime? ReceivedDate { get; set; }

    /// <summary>
    /// Statut de la commande dans le workflow.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>
    /// Notes relatives à la commande fournisseur.
    /// </summary>
    public string? Notes { get; set; }

    // Navigation Properties
    /// <summary>
    /// Vente parente à laquelle appartient cette commande fournisseur.
    /// </summary>
    public virtual Sale Sale { get; set; } = null!;

    /// <summary>
    /// Articles (verres) commandés au fournisseur.
    /// </summary>
    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}

