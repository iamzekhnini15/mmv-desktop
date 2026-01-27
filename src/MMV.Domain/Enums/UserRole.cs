namespace MMV.Domain.Enums;

/// <summary>
/// Représente le rôle d'un utilisateur dans le système.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Administrateur avec accès complet au système.
    /// </summary>
    Admin,

    /// <summary>
    /// Opticien pouvant gérer les clients, commandes et ventes.
    /// </summary>
    Optician,

    /// <summary>
    /// Technicien pour la fabrication et la préparation des commandes.
    /// </summary>
    Technician
}
