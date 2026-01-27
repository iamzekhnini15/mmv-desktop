using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente une vente effectuée (point de vente/caisse).
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
    /// Méthode de paiement utilisée.
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>
    /// Statut du paiement.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;

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
    /// Articles vendus dans cette transaction.
    /// </summary>
    public virtual ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}
