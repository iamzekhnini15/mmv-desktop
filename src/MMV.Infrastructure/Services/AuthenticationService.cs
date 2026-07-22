using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Domain.Validators;

namespace MMV.Infrastructure.Services;

/// <summary>
/// Implémentation du service d'authentification utilisant BCrypt pour le hachage des mots de passe.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Work factor BCrypt. 11 est un bon compromis entre sécurité et performance.
    /// </summary>
    private const int BcryptWorkFactor = 11;

    public AuthenticationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc/>
    public async Task<User?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Le nom d'utilisateur est requis.", nameof(username));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Le mot de passe est requis.", nameof(password));

        // P3-10 : recherche sur la forme normalisée du login (la normalisation appartient au repository, qui
        // délègue à UserIdentityPolicy). « admin », « Admin » et «  ADMIN  » atteignent donc le même compte.
        // Comportement d'authentification inchangé par ailleurs : BCrypt WF11, refus des comptes inactifs, et
        // résultat null uniforme qu'il s'agisse d'un compte inconnu, inactif ou d'un mot de passe erroné
        // (anti-énumération : le message d'erreur ne révèle pas l'existence d'un compte).
        var user = await _unitOfWork.Users.GetByNormalizedUsernameAsync(username, cancellationToken);

        if (user == null)
            return null;

        if (!user.IsActive)
            return null;

        if (!ValidatePassword(password, user.PasswordHash))
            return null;

        // Mettre à jour la dernière connexion
        await UpdateLastLoginAsync(user.UserId, cancellationToken);

        return user;
    }

    /// <inheritdoc/>
    public bool ValidatePassword(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Le mot de passe ne peut pas être vide.", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, BcryptWorkFactor);
    }

    /// <inheritdoc/>
    public async Task UpdateLastLoginAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ArgumentException("ID utilisateur invalide.", nameof(userId));

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user != null)
        {
            user.LastLogin = DateTime.UtcNow;
            await _unitOfWork.Users.UpdateAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            throw new ArgumentException("ID utilisateur invalide.", nameof(userId));
        if (string.IsNullOrWhiteSpace(currentPassword))
            throw new ArgumentException("Le mot de passe actuel est requis.", nameof(currentPassword));
        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("Le nouveau mot de passe est requis.", nameof(newPassword));

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return false;

        // Vérifier l'ancien mot de passe
        if (!ValidatePassword(currentPassword, user.PasswordHash))
            return false;

        // P3-10 — Politique appliquée APRÈS la vérification du mot de passe actuel et AVANT le nouveau hachage.
        //
        // Ordre délibéré : valider la politique d'abord révélerait, à un appelant ne connaissant pas le mot de
        // passe actuel, que le compte existe et que sa proposition était acceptable — une fuite d'information
        // gratuite. Le contrôle d'identité passe donc en premier ; la qualité du nouveau secret ensuite.
        //
        // Jusqu'ici, seul UserProfileViewModel appliquait la politique : tout appelant du service pouvait
        // remplacer un hash par celui d'un mot de passe faible (audit P3-10 §13, R1). Aucune écriture n'a
        // désormais lieu dans ce cas — le hash existant reste intact.
        //
        // Refus par exception typée, non par retour booléen : ce contrat renvoie déjà `false` pour « utilisateur
        // introuvable » et « mot de passe actuel erroné ». Y ajouter un troisième sens rendrait un mot de passe
        // faible indiscernable d'une erreur d'authentification, et l'UI existante afficherait un message faux.
        // BusinessRuleException porte le message stable du Domain ; aucune exception BCrypt n'est exposée.
        var (isValid, errorMessage) = UserValidator.ValidatePasswordPolicy(newPassword);
        if (!isValid)
            throw new BusinessRuleException(errorMessage);

        // Hasher et sauvegarder le nouveau mot de passe (nouveau sel à chaque changement, propriété de BCrypt :
        // réutiliser le même mot de passe produit donc un hash différent).
        user.PasswordHash = HashPassword(newPassword);
        await _unitOfWork.Users.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
