using Microsoft.EntityFrameworkCore;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Repository générique de base pour toutes les entités.
/// Fournit les opérations CRUD standard.
/// </summary>
/// <typeparam name="TEntity">Type d'entité</typeparam>
/// <typeparam name="TId">Type de la clé primaire</typeparam>
public abstract class BaseRepository<TEntity, TId> where TEntity : class
{
    /// <summary>
    /// DbContext partagé.
    /// </summary>
    protected readonly OpticDbContext _context;

    /// <summary>
    /// DbSet pour l'entité.
    /// </summary>
    protected readonly DbSet<TEntity> _dbSet;

    protected BaseRepository(OpticDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = context.Set<TEntity>();
    }

    /// <summary>
    /// Récupère une entité par son ID.
    /// </summary>
    public virtual async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
    }

    /// <summary>
    /// Récupère toutes les entités (sans tracking pour les lectures).
    /// </summary>
    public virtual async Task<IList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Ajoute une nouvelle entité.
    /// </summary>
    public virtual async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await _dbSet.AddAsync(entity, cancellationToken);
        return entity;
    }

    /// <summary>
    /// Met à jour une entité existante.
    /// </summary>
    public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        
        _dbSet.Update(entity);
        await Task.CompletedTask; // Simulation async
        return entity;
    }

    /// <summary>
    /// Supprime une entité par son ID.
    /// </summary>
    public virtual async Task DeleteAsync(TId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity != null)
        {
            _dbSet.Remove(entity);
        }
    }

    /// <summary>
    /// Supprime une entité.
    /// </summary>
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        
        _dbSet.Remove(entity);
        await Task.CompletedTask; // Simulation async
    }

    /// <summary>
    /// Vérifie si une entité existe par son ID.
    /// </summary>
    public virtual async Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return await _dbSet.FindAsync(new object[] { id }, cancellationToken) != null;
    }

    /// <summary>
    /// Compte le nombre total d'entités.
    /// </summary>
    protected async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Obtient un queryable pour des requêtes custom.
    /// </summary>
    protected IQueryable<TEntity> GetQueryable()
    {
        return _dbSet.AsNoTracking();
    }
}
