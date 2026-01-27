namespace MMV.Domain.Enums;

/// <summary>
/// Représente les méthodes de paiement acceptées.
/// </summary>
public enum PaymentMethod
{
    /// <summary>
    /// Paiement en espèces.
    /// </summary>
    Cash,

    /// <summary>
    /// Paiement par carte bancaire.
    /// </summary>
    Card,

    /// <summary>
    /// Paiement par chèque.
    /// </summary>
    Check,

    /// <summary>
    /// Virement bancaire.
    /// </summary>
    Transfer
}
