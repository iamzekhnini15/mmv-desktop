using MMV.Domain.Entities;

namespace MMV.Domain.Interfaces.Repositories;

/// <summary>
/// Interface du repository pour la gestion des fournisseurs.
/// </summary>
public interface ISupplierRepository : IGenericRepository<Supplier, long>
{
    /// <summary>
    /// Recherche un fournisseur par son nom.
    /// </summary>
    Task<Supplier?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recherche un fournisseur par son code de référence.
    /// </summary>
    Task<Supplier?> GetByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère un fournisseur avec tous ses produits.
    /// </summary>
    Task<Supplier?> GetWithProductsAsync(long supplierId, CancellationToken cancellationToken = default);
}
