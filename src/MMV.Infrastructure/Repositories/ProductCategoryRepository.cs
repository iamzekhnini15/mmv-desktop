using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des catégories de produits.
/// </summary>
public class ProductCategoryRepository : BaseRepository<ProductCategory, long>, IProductCategoryRepository
{
    public ProductCategoryRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Recherche une catégorie par son nom.
    /// </summary>
    public async Task<ProductCategory?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(pc => pc.Name == name, cancellationToken);
    }

    /// <summary>
    /// Récupère une catégorie avec tous ses produits.
    /// </summary>
    public async Task<ProductCategory?> GetWithProductsAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        return await _context.ProductCategories
            .Include(pc => pc.Products)
            .FirstOrDefaultAsync(pc => pc.CategoryId == categoryId, cancellationToken);
    }
}
