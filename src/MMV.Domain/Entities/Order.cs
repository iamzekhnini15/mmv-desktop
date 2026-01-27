using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente une commande client (workflow atelier).
/// </summary>
public class Order
{
    /// <summary>
    /// Identifiant unique de la commande.
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// Numéro de commande unique (ex: CMD-2026-0001).
    /// </summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Identifiant du client (optionnel).
    /// </summary>
    public long? CustomerId { get; set; }

    /// <summary>
    /// Identifiant du membre du personnel ayant créé la commande.
    /// </summary>
    public long? StaffId { get; set; }

    /// <summary>
    /// Date et heure de création de la commande.
    /// </summary>
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date estimée de livraison.
    /// </summary>
    public DateTime? EstimatedDelivery { get; set; }

    /// <summary>
    /// Montant total de la commande (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>
    /// Statut de la commande dans le workflow.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>
    /// Notes relatives à la commande.
    /// </summary>
    public string? Notes { get; set; }

    // Navigation Properties
    /// <summary>
    /// Client associé à cette commande.
    /// </summary>
    public virtual Customer? Customer { get; set; }

    /// <summary>
    /// Membre du personnel ayant créé la commande.
    /// </summary>
    public virtual User? Staff { get; set; }

    /// <summary>
    /// Articles composant cette commande.
    /// </summary>
    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
