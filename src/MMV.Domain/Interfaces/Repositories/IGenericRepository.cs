using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface de base pour tous les repositories (pattern générique).
/// </summary>
/// <typeparam name="TEntity">Type d'entité</typeparam>
/// <typeparam name="TId">Type de la clé primaire</typeparam>
public interface IGenericRepository<TEntity, TId> where TEntity : class
{
    /// <summary>
    /// Récupère une entité par son ID.
    /// </summary>
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère toutes les entités.
    /// </summary>
    Task<IList<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ajoute une nouvelle entité.
    /// </summary>
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Met à jour une entité existante.
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime une entité par son ID.
    /// </summary>
    Task DeleteAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Supprime une entité.
    /// </summary>
    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérifie si une entité existe par son ID.
    /// </summary>
    Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);
}
