using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

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

        var user = await _unitOfWork.Users.GetByUsernameAsync(username.Trim(), cancellationToken);

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

        // Hasher et sauvegarder le nouveau mot de passe
        user.PasswordHash = HashPassword(newPassword);
        await _unitOfWork.Users.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
