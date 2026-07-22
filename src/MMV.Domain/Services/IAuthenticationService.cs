using MMV.Domain.Entities;

namespace MMV.Domain.Services;

/// <summary>
/// Service d'authentification pour la gestion des mots de passe et la connexion des utilisateurs.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Authentifie un utilisateur avec son nom d'utilisateur et son mot de passe.
    /// </summary>
    /// <param name="username">Nom d'utilisateur.</param>
    /// <param name="password">Mot de passe en clair.</param>
    /// <returns>L'utilisateur authentifié ou null si les identifiants sont invalides.</returns>
    Task<User?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valide un mot de passe en clair contre un hash BCrypt.
    /// </summary>
    bool ValidatePassword(string password, string hash);

    /// <summary>
    /// Génère un hash BCrypt à partir d'un mot de passe en clair.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    /// Met à jour la date de dernière connexion d'un utilisateur.
    /// </summary>
    Task UpdateLastLoginAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change le mot de passe d'un utilisateur après vérification de l'ancien.
    /// </summary>
    /// <returns>True si le changement a réussi, false si l'ancien mot de passe est incorrect.</returns>
    /// <exception cref="MMV.Domain.Exceptions.BusinessRuleException">
    /// P3-10 — Le nouveau mot de passe ne respecte pas la politique
    /// (<see cref="MMV.Domain.Validators.UserValidator.ValidatePasswordPolicy"/>) : <b>aucune écriture</b> n'a eu
    /// lieu, le hash existant est intact. Le refus est porté par une exception typée plutôt que par
    /// <c>false</c>, qui signifie déjà « utilisateur introuvable ou mot de passe actuel erroné » — un mot de
    /// passe faible y serait indiscernable d'une erreur d'authentification.
    /// </exception>
    Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
