using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Policies;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des utilisateurs.
/// </summary>
public class UserRepository : BaseRepository<User, long>, IUserRepository
{
    public UserRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère un utilisateur par son identifiant de connexion, quelle qu'en soit la casse (P3-10).
    /// </summary>
    /// <remarks>
    /// Le filtre porte <b>directement</b> sur la colonne indexée <c>NormalizedUsername</c> : la normalisation est
    /// faite en C# par <see cref="UserIdentityPolicy"/> avant la requête, jamais par une fonction SQL appliquée à
    /// <c>Username</c> — une telle expression serait non-SARGable (index inutilisable, table entière parcourue) et
    /// dépendrait de la collation du provider. <c>GetQueryable()</c> applique <c>AsNoTracking</c> : la lecture est
    /// fraîche, ce qui garantit qu'une désactivation faite sur un autre poste est vue à la connexion suivante.
    /// </remarks>
    public async Task<User?> GetByNormalizedUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);

        var normalized = UserIdentityPolicy.NormalizeUsername(username);

        return await GetQueryable()
            .FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, cancellationToken);
    }

    /// <summary>
    /// Indique si un <b>autre</b> utilisateur porte déjà cet identifiant de connexion (P3-10).
    /// </summary>
    /// <remarks>
    /// <c>AnyAsync</c> sur la colonne indexée : aucune entité matérialisée, aucune lecture complète de la table.
    /// <c>AsNoTracking</c> évite qu'une entité déjà suivie et modifiée fausse le contrôle d'unicité.
    /// </remarks>
    public async Task<bool> ExistsByNormalizedUsernameAsync(
        string username, long? excludingUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(username);

        var normalized = UserIdentityPolicy.NormalizeUsername(username);

        return await _dbSet
            .AsNoTracking()
            .AnyAsync(
                u => u.NormalizedUsername == normalized
                     && (excludingUserId == null || u.UserId != excludingUserId),
                cancellationToken);
    }

    /// <summary>
    /// Détache <paramref name="user"/> du <see cref="Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker"/>
    /// si le contexte le suit encore (P3-10 — revue ciblée avant commit).
    /// </summary>
    /// <remarks>
    /// Recherche par référence via <c>Entry(user)</c> : fonctionne aussi bien pour une entité candidate à la
    /// création (clé temporaire, <c>UserId == 0</c>) que pour une entité chargée puis mutée en place (Update).
    /// Ne touche que cette entité précise — aucune autre entrée du tracker n'est affectée, aucune requête SQL
    /// n'est émise (opération en mémoire).
    /// </remarks>
    public void DetachIfTracked(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var entry = _context.Entry(user);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
        }
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
