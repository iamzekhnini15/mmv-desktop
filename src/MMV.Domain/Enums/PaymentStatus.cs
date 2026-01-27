namespace MMV.Domain.Enums;

/// <summary>
/// Représente le statut d'un paiement.
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Paiement effectué en totalité.
    /// </summary>
    Paid,

    /// <summary>
    /// Paiement partiel reçu.
    /// </summary>
    Partial,

    /// <summary>
    /// Paiement remboursé.
    /// </summary>
    Refunded,

    /// <summary>
    /// En attente de paiement.
    /// </summary>
    Pending
}
