using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente une vente complète (tous les produits : monture + verres + accessoires).
/// C'est la transaction globale avec le client.
/// </summary>
public class Sale
{
    /// <summary>
    /// Identifiant unique de la vente.
    /// </summary>
    public long SaleId { get; set; }

    /// <summary>
    /// Numéro de vente unique (ex: VTE-2026-0001).
    /// </summary>
    public string SaleNumber { get; set; } = string.Empty;

    /// <summary>
    /// Identifiant du client (optionnel).
    /// </summary>
    public long? CustomerId { get; set; }

    /// <summary>
    /// Identifiant du membre du personnel ayant effectué la vente.
    /// </summary>
    public long? StaffId { get; set; }

    /// <summary>
    /// Date et heure de la vente.
    /// </summary>
    public DateTime SaleDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date estimée de livraison (si fabrication nécessaire).
    /// </summary>
    public DateTime? EstimatedDelivery { get; set; }

    /// <summary>
    /// Montant total avant remise (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Montant de la remise appliquée (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Montant final après remise (OBLIGATOIRE: type decimal pour argent).
    /// </summary>
    public decimal FinalAmount { get; set; }

    /// <summary>
    /// Montant de l'acompte versé par le client.
    /// </summary>
    public decimal? DepositAmount { get; set; }

    /// <summary>
    /// Montant restant à payer (calculé : FinalAmount - DepositAmount).
    /// </summary>
    public decimal? RemainingAmount { get; set; }

    /// <summary>
    /// Méthode de paiement utilisée.
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// Statut du paiement.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;

    /// <summary>
    /// Statut de la vente dans le workflow (Draft, AwaitingLenses, InFabrication, Ready, Delivered).
    /// </summary>
    public SaleStatus Status { get; set; } = SaleStatus.Draft;

    /// <summary>
    /// Notes relatives à la vente.
    /// </summary>
    public string? Notes { get; set; }

    // Navigation Properties
    /// <summary>
    /// Client ayant effectué l'achat.
    /// </summary>
    public virtual Customer? Customer { get; set; }

    /// <summary>
    /// Membre du personnel ayant enregistré la vente.
    /// </summary>
    public virtual User? Staff { get; set; }

    /// <summary>
    /// Articles vendus dans cette transaction (monture, verres, accessoires).
    /// </summary>
    public virtual ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();

    /// <summary>
    /// Commandes fournisseurs associées (pour les verres uniquement).
    /// </summary>
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}

