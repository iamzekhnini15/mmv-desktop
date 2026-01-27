using MMV.Domain.Entities;
using MMV.Domain.Enums;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des utilisateurs.
/// </summary>
public interface IUserRepository : IGenericRepository<User, long>
{
    /// <summary>
    /// Récupère un utilisateur par son nom d'utilisateur.
    /// </summary>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère tous les utilisateurs actifs.
    /// </summary>
    Task<IList<User>> GetActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les utilisateurs par rôle.
    /// </summary>
    Task<IList<User>> GetByRoleAsync(UserRole role, CancellationToken cancellationToken = default);
}
