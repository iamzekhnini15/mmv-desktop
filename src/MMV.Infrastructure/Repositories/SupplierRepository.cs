using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des fournisseurs.
/// </summary>
public class SupplierRepository : BaseRepository<Supplier, long>, ISupplierRepository
{
    public SupplierRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Recherche un fournisseur par son nom.
    /// </summary>
    public async Task<Supplier?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(s => s.Name == name, cancellationToken);
    }

    /// <summary>
    /// Recherche un fournisseur par son code de référence.
    /// </summary>
    public async Task<Supplier?> GetByReferenceCodeAsync(string referenceCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referenceCode);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(s => s.ReferenceCode == referenceCode, cancellationToken);
    }

    /// <summary>
    /// Récupère un fournisseur avec tous ses produits.
    /// </summary>
    public async Task<Supplier?> GetWithProductsAsync(long supplierId, CancellationToken cancellationToken = default)
    {
        return await _context.Suppliers
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.SupplierId == supplierId, cancellationToken);
    }
}
