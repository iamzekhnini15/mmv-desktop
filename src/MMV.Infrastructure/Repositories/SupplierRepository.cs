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

    /// <inheritdoc />
    public async Task<bool> TryDeleteIfUnusedAsync(long supplierId, CancellationToken cancellationToken = default)
    {
        // Suppression conditionnelle atomique (P3-9) : « ce fournisseur existe ET aucun produit ne le référence »
        // est évalué par la MÊME instruction SQL que le DELETE — traduit en
        // DELETE FROM "Suppliers" WHERE "SupplierId" = @p AND NOT EXISTS (SELECT 1 FROM "Products" …).
        // Aucun « check-then-act » : un produit créé concurremment entre une vérification et l'écriture est vu par
        // la condition elle-même. s.Products couvre les produits ACTIFS ET INACTIFS (aucun filtre IsActive) : un
        // produit désactivé garde sa ligne, donc sa FK.
        //
        // ExecuteDeleteAsync contourne délibérément le change tracker : la décision porte sur l'état réel de la
        // base, jamais sur une navigation déjà chargée en mémoire.
        var rowsAffected = await _context.Suppliers
            .Where(s => s.SupplierId == supplierId && !s.Products.Any())
            .ExecuteDeleteAsync(cancellationToken);

        var deleted = rowsAffected == 1;
        if (deleted)
        {
            // ExecuteDeleteAsync contourne le change tracker : une entité Supplier chargée plus tôt dans la MÊME
            // portée (ex. GetByIdAsync) resterait autrement suivie (Unchanged) après une suppression physique
            // réussie, et un FindAsync ultérieur sur ce contexte la ressusciterait sans requêter la base (preuve
            // empirique, revue ciblée avant commit P3-9). Détachement CIBLÉ sur l'identifiant supprimé uniquement —
            // aucun ChangeTracker.Clear() global, aucun produit ni fournisseur voisin affecté, aucune requête SQL
            // supplémentaire (opération en mémoire sur le tracker).
            var tracked = _context.ChangeTracker.Entries<Supplier>()
                .FirstOrDefault(e => e.Entity.SupplierId == supplierId);
            if (tracked != null)
            {
                tracked.State = EntityState.Detached;
            }
        }

        return deleted;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsFreshAsync(long supplierId, CancellationToken cancellationToken = default)
    {
        // AnyAsync + AsNoTracking : requête SQL réelle. L'ExistsAsync hérité de BaseRepository repose sur
        // FindAsync et renverrait une entité déjà suivie dans la portée sans toucher la base (P3-9 §15).
        return await _context.Suppliers
            .AsNoTracking()
            .AnyAsync(s => s.SupplierId == supplierId, cancellationToken);
    }
}
