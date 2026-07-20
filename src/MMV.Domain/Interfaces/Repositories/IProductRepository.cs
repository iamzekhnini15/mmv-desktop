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
    /// Met à jour les champs <b>catalogue</b> d'un produit (nom, référence, prix, catégorie, fournisseur, détails)
    /// <b>sans jamais réécrire <see cref="Product.StockQuantity"/></b> (P3-5). L'édition catalogue ne doit pas
    /// réécrire une quantité chargée précédemment dans un formulaire : cela écraserait tout décrément concurrent
    /// survenu entre-temps (lost update). Le stock ne change désormais que par les use cases de <b>mouvement</b>
    /// (décrément sûr, incrément atomique, ajustement concurrent-safe).
    /// </summary>
    Task UpdateCatalogAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>
    /// Récupère un produit par ID avec tous ses détails (GlassDetail, LensDetail, AccessoryDetail).
    /// </summary>
    Task<Product?> GetByIdWithDetailsAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Prise atomique conditionnelle</b> d'un produit <b>actif</b> (P3-7) : réussit uniquement si le produit
    /// <paramref name="productId"/> existe <b>et</b> est <b>actif</b> au moment exact de l'écriture, et maintient
    /// la ligne prise jusqu'au commit de la transaction englobante.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pourquoi ici et pas dans <c>IStockMutationService</c>.</b> La garde « produit actif » est une règle de
    /// <b>vente</b>, pas une règle de mutation de stock : une commande historique doit rester fabricable même si
    /// son produit a été désactivé entre-temps (le décrément de fabrication passe par
    /// <c>AdvanceOrderStatusUseCase</c>). Exiger <c>IsActive</c> globalement au niveau du décrément bloquerait
    /// cette fabrication légitime ; la contrainte est donc portée par le flux de vente seul.
    /// </para>
    /// <para>
    /// <b>Écriture sans changement fonctionnel.</b> La prise réaffecte à <c>IsActive</c> la valeur qu'il doit déjà
    /// avoir (<c>true</c>) : aucune donnée catalogue n'est modifiée — en particulier jamais
    /// <c>StockQuantity</c> —, seule la ligne est prise jusqu'au commit.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>true</c> si le produit actif a été pris (1 ligne affectée) ; <c>false</c> si aucune ligne n'a été prise
    /// (produit introuvable <b>ou</b> désactivé, y compris désactivé concurremment depuis l'affichage).
    /// </returns>
    Task<bool> TryAcquireActiveAsync(long productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lecture <b>fraîche et non suivie</b> d'un produit par identifiant (P3-7, revue avant commit) : contourne le
    /// change tracker, donc ne peut jamais renvoyer une entité déjà suivie et périmée dans le <c>DbContext</c> de
    /// la portée. À utiliser pour toute décision (catégorie réelle, existence) qui doit refléter l'état réel de la
    /// base après une <see cref="TryAcquireActiveAsync"/> — jamais <c>GetByIdAsync</c> (qui peut renvoyer une
    /// instance suivie depuis le cache local du contexte sans requêter la base, via <c>FindAsync</c>).
    /// </summary>
    Task<Product?> GetByIdFreshAsync(long productId, CancellationToken cancellationToken = default);
}
