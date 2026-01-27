using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des catégories de produits.
/// </summary>
public interface IProductCategoryRepository : IGenericRepository<ProductCategory, long>
{
    /// <summary>
    /// Recherche une catégorie par son nom.
    /// </summary>
    Task<ProductCategory?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère une catégorie avec tous ses produits.
    /// </summary>
    Task<ProductCategory?> GetWithProductsAsync(long categoryId, CancellationToken cancellationToken = default);
}
