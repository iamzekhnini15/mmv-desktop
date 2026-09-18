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
    /// Date de naissance — <b>date civile</b>, pas un instant (P4-5D / ADR-PROD-DB-004 §5, décision 7).
    ///
    /// <para>
    /// Le type <see cref="DateOnly"/> exprime ce que la donnée <b>est</b>. Tant qu'elle était un
    /// <c>DateTime</c>, toute conversion de fuseau pouvait <b>changer le jour</b> : une naissance saisie
    /// à 00:00 locale devenait la veille à 22:00 en heure d'été française. Sur une date de naissance,
    /// c'est une erreur métier, pas un détail de présentation (ADR-PROD-DB-004 §2.4).
    /// </para>
    ///
    /// <para>
    /// Conséquence directe : ce champ n'est <b>ni converti en UTC, ni soumis au convertisseur d'instants</b>
    /// (<c>UtcDateTimeConverter</c>). Il est stocké <c>date</c> sur PostgreSQL et <c>TEXT yyyy-MM-dd</c>
    /// sur SQLite.
    /// </para>
    /// </summary>
    public DateOnly? BirthDate { get; set; }

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

    /// <summary>
    /// Indique si la fiche client est archivée (P3-2B). Un client archivé conserve l'intégralité de son
    /// historique (ordonnances et ventes) mais est exclu par défaut des listes et des sélecteurs.
    /// L'état ne se modifie que par <see cref="Archive"/> / <see cref="Reactivate"/>.
    /// </summary>
    public bool IsArchived { get; private set; }

    /// <summary>
    /// Archive le client (idempotent). N'altère aucun historique et ne supprime rien.
    /// L'horodatage <see cref="UpdatedAt"/> reste à la charge de la couche Application (convention du dépôt).
    /// </summary>
    public void Archive() => IsArchived = true;

    /// <summary>
    /// Réactive un client archivé (idempotent).
    /// L'horodatage <see cref="UpdatedAt"/> reste à la charge de la couche Application (convention du dépôt).
    /// </summary>
    public void Reactivate() => IsArchived = false;

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
