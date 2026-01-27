using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des utilisateurs.
/// </summary>
public class UserRepository : BaseRepository<User, long>, IUserRepository
{
    public UserRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère un utilisateur par son nom d'utilisateur.
    /// </summary>
    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    /// <summary>
    /// Récupère tous les utilisateurs actifs.
    /// </summary>
    public async Task<IList<User>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(u => u.IsActive)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les utilisateurs par rôle.
    /// </summary>
    public async Task<IList<User>> GetByRoleAsync(UserRole role, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(u => u.Role == role && u.IsActive)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync(cancellationToken);
    }
}
