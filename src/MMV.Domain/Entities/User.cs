using MMV.Domain.Enums;

namespace MMV.Domain.Entities;

/// <summary>
/// Représente un utilisateur du système (membre du personnel).
/// </summary>
public class User
{
    /// <summary>
    /// Identifiant unique de l'utilisateur.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Nom d'utilisateur pour la connexion (unique).
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Hash du mot de passe (stocké de manière sécurisée).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Prénom de l'utilisateur.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Nom de famille de l'utilisateur.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Rôle de l'utilisateur dans le système.
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// Indique si le compte utilisateur est actif.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Date et heure de la dernière connexion.
    /// </summary>
    public DateTime? LastLogin { get; set; }

    /// <summary>
    /// Date et heure de création du compte.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    /// <summary>
    /// Ventes effectuées par cet utilisateur.
    /// </summary>
    public virtual ICollection<Sale> Sales { get; set; } = new List<Sale>();

    /// <summary>
    /// Mouvements de stock effectués par cet utilisateur.
    /// </summary>
    public virtual ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
