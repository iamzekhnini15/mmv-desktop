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
    /// Indique s'il existe déjà un produit portant la <b>référence normalisée</b> donnée, en excluant
    /// éventuellement un produit (utile en modification pour ignorer le produit courant). Requête compacte
    /// (<c>AnyAsync</c>) — aucune collection matérialisée.
    /// </summary>
    /// <param name="normalizedReference">Référence déjà normalisée (cf. <see cref="Product.NormalizeReference"/>).</param>
    /// <param name="excludingProductId">Identifiant à exclure (0 en création : aucun produit exclu).</param>
    Task<bool> ExistsByNormalizedReferenceAsync(string normalizedReference, long excludingProductId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indique si le produit est utilisé par au moins une ligne d'historique (vente, commande ou mouvement de
    /// stock). Garde-fou de suppression (P3-4B) : requête compacte (<c>AnyAsync</c> à court-circuit), aucune
    /// collection matérialisée.
    /// </summary>
    Task<bool> IsReferencedByHistoryAsync(long productId, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Récupère un produit par ID avec tous ses détails (GlassDetail, LensDetail, AccessoryDetail).
    /// </summary>
    Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default);
}
