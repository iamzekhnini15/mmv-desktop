using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des produits.
/// </summary>
public interface IProductRepository : IGenericRepository<Product, long>
{
    /// <summary>
    /// Recherche un produit par sa référence.
    /// </summary>
    Task<Product?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les produits d'une catégorie.
    /// </summary>
    Task<IList<Product>> GetByCategoryAsync(long categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les produits d'un fournisseur.
    /// </summary>
    Task<IList<Product>> GetBySupplierAsync(long supplierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les produits avec un stock inférieur au seuil d'alerte.
    /// </summary>
    Task<IList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche des produits par nom (partiel).
    /// </summary>
    Task<IList<Product>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère les produits actifs uniquement.
    /// </summary>
    Task<IList<Product>> GetActiveProductsAsync(CancellationToken cancellationToken = default);
}
