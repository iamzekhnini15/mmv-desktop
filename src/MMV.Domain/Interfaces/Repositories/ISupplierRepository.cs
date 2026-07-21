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

    /// <summary>
    /// Supprime le fournisseur <b>si et seulement si aucun produit ne le référence</b>, en une <b>seule opération
    /// atomique</b> (P3-9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// La condition « aucun produit lié » est évaluée par la <b>même instruction</b> que la suppression : un
    /// produit créé sur un autre poste entre une vérification préalable et l'écriture ne peut donc pas être perdu
    /// de vue. C'est le motif de prise atomique conditionnelle déjà établi par
    /// <c>IProductRepository.TryAcquireActiveAsync</c> et <c>ICustomerRepository.TryAcquireActiveAsync</c> (P3-7),
    /// appliqué ici à une suppression.
    /// </para>
    /// <para>
    /// <b>Tous</b> les produits comptent — actifs comme inactifs. Un produit désactivé conserve sa ligne, donc sa
    /// clé étrangère : l'ignorer ferait annoncer « suppression possible » puis échouer en base (audit P3-9 §11.11).
    /// </para>
    /// <para>
    /// La clé étrangère <c>Product → Supplier</c> reste en <c>Restrict</c> : elle demeure le filet ultime contre
    /// une régression de configuration ou un chemin de suppression alternatif.
    /// </para>
    /// </remarks>
    /// <returns>
    /// <c>true</c> si la ligne fournisseur a été supprimée (1 ligne affectée) ; <c>false</c> si rien n'a été
    /// supprimé — fournisseur <b>introuvable</b> <b>ou</b> encore <b>référencé</b> par au moins un produit. Le
    /// booléen ne distingue pas ces deux cas : c'est au use case de les départager par une lecture de diagnostic
    /// <b>a posteriori</b>, qui ne décide jamais de l'écriture.
    /// </returns>
    Task<bool> TryDeleteIfUnusedAsync(long supplierId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Test d'existence <b>frais et non suivi</b> : interroge réellement la base (<c>AnyAsync</c>) sans passer par
    /// le change tracker (P3-9).
    /// </summary>
    /// <remarks>
    /// À utiliser partout où l'existence doit refléter l'état <b>réel</b> de la base — diagnostic après
    /// <see cref="TryDeleteIfUnusedAsync"/>, garde de fournisseur obligatoire dans les écritures produit. Ni
    /// <c>GetByIdAsync</c> ni l'<c>ExistsAsync</c> hérité de <see cref="IGenericRepository{TEntity,TId}"/> ne
    /// conviennent : tous deux reposent sur <c>FindAsync</c>, qui renvoie une entité déjà suivie dans la portée
    /// sans requêter la base — donc potentiellement périmée après une modification concurrente. Même motif que
    /// <c>IProductRepository.GetByIdFreshAsync</c> (P3-7, revue avant commit).
    /// </remarks>
    Task<bool> ExistsFreshAsync(long supplierId, CancellationToken cancellationToken = default);
}
