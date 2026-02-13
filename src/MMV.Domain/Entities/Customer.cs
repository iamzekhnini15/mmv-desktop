namespace MMV.Domain.Entities;

/// <summary>
/// Représente un client du magasin d'optique.
/// </summary>
public class Customer
{
    /// <summary>
    /// Identifiant unique du client.
    /// </summary>
    public long CustomerId { get; set; }

    /// <summary>
    /// Prénom du client.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Nom de famille du client.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Date de naissance.
    /// </summary>
    public DateTime? BirthDate { get; set; }

    /// <summary>
    /// Numéro de téléphone.
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Adresse email.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Adresse postale.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Ville de résidence.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Code postal.
    /// </summary>
    public string? PostalCode { get; set; }

    /// <summary>
    /// Numéro de sécurité sociale.
    /// </summary>
    public string? SocialSecurityNumber { get; set; }

    /// <summary>
    /// Nom de la mutuelle/assurance.
    /// </summary>
    public string? InsuranceName { get; set; }

    /// <summary>
    /// Notes diverses concernant le client.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Date de création de la fiche client.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Date de dernière modification.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    /// <summary>
    /// Prescriptions médicales du client.
    /// </summary>
    public virtual ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();

    /// <summary>
    /// Ventes effectuées au client.
    /// </summary>
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();
}
