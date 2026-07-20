using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des produits.
/// </summary>
public class ProductRepository : BaseRepository<Product, long>, IProductRepository
{
    public ProductRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Surcharge GetAllAsync pour inclure les relations.
    /// </summary>
    public override async Task<IList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Include(p => p.ProductCategory)
            .Include(p => p.Supplier)
            .Include(p => p.GlassDetail)
            .Include(p => p.LensDetail)
            .Include(p => p.AccessoryDetail)
            .Include(p => p.OrderItems)
                .ThenInclude(oi => oi.Order)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recherche un produit par sa référence.
    /// </summary>
    public async Task<Product?> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(p => p.Reference == reference, cancellationToken);
    }

    /// <summary>
    /// Indique s'il existe déjà un produit portant la référence normalisée donnée (hors produit exclu).
    /// </summary>
    public async Task<bool> ExistsByNormalizedReferenceAsync(string normalizedReference, long excludingProductId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedReference);

        return await _dbSet
            .AsNoTracking()
            .AnyAsync(p => p.NormalizedReference == normalizedReference && p.ProductId != excludingProductId, cancellationToken);
    }

    /// <summary>
    /// Indique si le produit est utilisé par au moins une ligne de vente, de commande ou un mouvement de stock.
    /// </summary>
    public async Task<bool> IsReferencedByHistoryAsync(long productId, CancellationToken cancellationToken = default)
    {
        return await _context.SaleItems.AsNoTracking().AnyAsync(si => si.ProductId == productId, cancellationToken)
            || await _context.OrderItems.AsNoTracking().AnyAsync(oi => oi.ProductId == productId, cancellationToken)
            || await _context.StockMovements.AsNoTracking().AnyAsync(sm => sm.ProductId == productId, cancellationToken);
    }

    /// <summary>
    /// Récupère les produits d'une catégorie.
    /// </summary>
    public async Task<IList<Product>> GetByCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.CategoryId == categoryId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les produits d'un fournisseur.
    /// </summary>
    public async Task<IList<Product>> GetBySupplierAsync(long supplierId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.SupplierId == supplierId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les produits avec un stock inférieur au seuil d'alerte.
    /// </summary>
    public async Task<IList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.IsActive && p.StockQuantity < p.StockAlertThreshold)
            .OrderBy(p => p.StockQuantity)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Produits <b>actifs</b> au niveau ou en dessous du seuil d'alerte (P3-8) : population exacte des alertes de
    /// stock bas, filtrée <b>en SQL</b>.
    /// </summary>
    /// <remarks>
    /// N'emprunte pas le <c>GetQueryable()</c> privé : celui-ci charge cinq navigations (catégorie, fournisseur,
    /// détails verre/lentille/accessoire) dont la génération d'alertes n'utilise aucune. Seules les colonnes de la
    /// table <c>Products</c> sont lues. Tri par <c>ProductId</c> : ordre déterministe et stable, indépendant du
    /// stock — donc reproductible d'une exécution à l'autre.
    /// </remarks>
    public async Task<IReadOnlyList<Product>> GetActiveLowStockAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity <= p.StockAlertThreshold)
            .OrderBy(p => p.ProductId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Recherche des produits par nom (partiel).
    /// </summary>
    public async Task<IList<Product>> SearchByNameAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchTerm);
        
        var lowerSearchTerm = searchTerm.ToLower();
        
        return await GetQueryable()
            .Where(p => p.IsActive && p.Name.ToLower().Contains(lowerSearchTerm))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les produits actifs uniquement.
    /// </summary>
    public async Task<IList<Product>> GetActiveProductsAsync(CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Obtient un queryable avec les relations de base chargées.
    /// </summary>
    private new IQueryable<Product> GetQueryable()
    {
        return _dbSet
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.GlassDetail)
            .Include(p => p.LensDetail)
            .Include(p => p.AccessoryDetail);
    }

    /// <summary>
    /// Met à jour les champs catalogue d'un produit <b>sans réécrire <see cref="Product.StockQuantity"/></b> (P3-5).
    /// Marque toutes les colonnes modifiées (comme <see cref="BaseRepository{TEntity,TId}.UpdateAsync"/>) puis
    /// <b>exclut explicitement</b> <c>StockQuantity</c> de l'ordre <c>UPDATE</c> généré (<c>IsModified = false</c>).
    /// Ainsi une édition catalogue concurrente n'écrase jamais un décrément survenu entre le chargement du
    /// formulaire et l'enregistrement (élimination du <i>lost update</i> sur le stock).
    /// </summary>
    public async Task UpdateCatalogAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        _dbSet.Update(product);
        _context.Entry(product).Property(p => p.StockQuantity).IsModified = false;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Récupère un produit par ID avec tous ses détails et tracking activé pour l'édition.
    /// </summary>
    public async Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Include(p => p.Supplier)
            .Include(p => p.GlassDetail)
            .Include(p => p.LensDetail)
            .Include(p => p.AccessoryDetail)
            .Include(p => p.OrderItems)
                .ThenInclude(oi => oi.Order)
            .FirstOrDefaultAsync(p => p.ProductId == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryAcquireActiveAsync(long productId, CancellationToken cancellationToken = default)
    {
        // Prise atomique conditionnelle (P3-7) : « existe ET actif » est évalué par la MÊME instruction que
        // l'écriture, ce qui refuse une désactivation concurrente survenue depuis l'affichage du panier. La valeur
        // écrite est celle que la ligne doit déjà porter (IsActive = true) : aucune donnée catalogue n'est
        // modifiée — en particulier jamais StockQuantity (P3-5) —, la mise à jour sert uniquement de prise de
        // ligne, maintenue jusqu'au commit.
        var rowsAffected = await _context.Products
            .Where(p => p.ProductId == productId && p.IsActive)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.IsActive, true),
                cancellationToken);

        return rowsAffected == 1;
    }

    /// <inheritdoc />
    public async Task<Product?> GetByIdFreshAsync(long productId, CancellationToken cancellationToken = default)
    {
        // AsNoTracking : contourne délibérément le change tracker. GetByIdAsync (FindAsync hérité) renverrait
        // sinon une entité déjà suivie dans le DbContext de la portée sans requêter la base (P3-7, revue avant
        // commit) — un risque réel dès lors qu'un même contexte peut avoir chargé ce produit avec tracking
        // ailleurs dans la même portée (p. ex. un écran catalogue resté ouvert), avec une Category potentiellement
        // périmée décidant à tort du moment du décrément de stock.
        return await _dbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId, cancellationToken);
    }
}
