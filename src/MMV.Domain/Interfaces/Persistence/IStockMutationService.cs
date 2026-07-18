namespace MMV.Domain.Interfaces.Persistence;

/// <summary>
/// Primitive d'écriture <b>sûre</b> pour les mutations de quantité de stock (P2A-1D, R-09).
///
/// <para>
/// Objectif : remplacer le motif <i>lecture-modification-écriture</i> non borné et non atomique des
/// décréments de stock (par ex. <c>product.StockQuantity -= qty; UpdateAsync(product)</c>) par une
/// opération <b>atomique conditionnelle</b> qui :
/// <list type="bullet">
///   <item>ne rend <b>jamais</b> le stock négatif ;</item>
///   <item>élimine la <b>mise à jour perdue</b> (deux décréments concurrents ne peuvent pas écraser la
///         valeur l'un de l'autre) ;</item>
///   <item>prend en compte un stock <b>modifié entre-temps</b> (la décision est prise au moment de
///         l'écriture, pas au moment de la lecture).</item>
/// </list>
/// </para>
///
/// <para>
/// Abstraction volontairement neutre (aucune dépendance EF/SQLite) afin de rester compatible avec la
/// future couche Application / use cases (cf. ADR-stock-concurrency, ADR-010, R-09) : l'implémentation
/// participe à la transaction courante ouverte par <see cref="ITransactionRunner"/> (même
/// <c>DbContext</c> de portée), si bien qu'un échec de décrément annule l'écriture composée
/// (vente + commande + mouvements de stock).
/// </para>
/// </summary>
public interface IStockMutationService
{
    /// <summary>
    /// Décrémente atomiquement le stock du produit <paramref name="productId"/> de
    /// <paramref name="quantity"/> unités, <b>uniquement si</b> le stock disponible le permet.
    /// </summary>
    /// <param name="productId">Identifiant du produit.</param>
    /// <param name="quantity">Quantité à retirer (doit être strictement positive).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// si <paramref name="quantity"/> est inférieure ou égale à zéro (erreur d'appel).
    /// </exception>
    /// <exception cref="MMV.Domain.Exceptions.InsufficientStockException">
    /// si le stock disponible est insuffisant (ou le produit introuvable, ou le stock modifié
    /// concurremment) : <b>aucune</b> modification n'est persistée.
    /// </exception>
    Task DecrementStockAsync(long productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrémente atomiquement le stock du produit <paramref name="productId"/> de
    /// <paramref name="quantity"/> unités (P3-5). Remplace le motif <i>lecture-modification-écriture</i>
    /// (<c>StockQuantity += quantity</c>) par une mise à jour atomique <c>SET StockQuantity = StockQuantity + q</c> :
    /// deux entrées concurrentes ne peuvent plus perdre l'une des quantités.
    /// </summary>
    /// <param name="productId">Identifiant du produit.</param>
    /// <param name="quantity">Quantité à ajouter (doit être strictement positive).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>Le stock résultant (valeur relue après incrément).</returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// si <paramref name="quantity"/> est inférieure ou égale à zéro (erreur d'appel).
    /// </exception>
    /// <exception cref="MMV.Domain.Exceptions.EntityNotFoundException">
    /// si le produit est introuvable (aucune ligne affectée).
    /// </exception>
    Task<int> IncrementStockAsync(long productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fixe le stock du produit <paramref name="productId"/> à la valeur comptée
    /// <paramref name="targetQuantity"/> (ajustement d'inventaire, P3-5), de façon <b>concurrent-safe</b> :
    /// la valeur courante est lue, puis une mise à jour conditionnelle (<c>WHERE StockQuantity = valeur lue</c>)
    /// est tentée. Si un autre poste a modifié le stock entre la lecture et l'écriture, l'opération échoue
    /// avec <see cref="MMV.Domain.Exceptions.StockConcurrencyConflictException"/> — jamais de
    /// <i>last-write-wins</i> silencieux.
    /// </summary>
    /// <param name="productId">Identifiant du produit.</param>
    /// <param name="targetQuantity">Quantité comptée cible (valeur absolue, doit être ≥ 0 ; zéro autorisé).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <returns>
    /// Un <see cref="StockAdjustmentResult"/> exposant l'ancienne quantité, la nouvelle quantité et le delta
    /// signé réel (pour tracer le mouvement <c>Adjustment</c>).
    /// </returns>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// si <paramref name="targetQuantity"/> est strictement négative (erreur d'appel).
    /// </exception>
    /// <exception cref="MMV.Domain.Exceptions.EntityNotFoundException">
    /// si le produit est introuvable au moment de la lecture.
    /// </exception>
    /// <exception cref="MMV.Domain.Exceptions.StockConcurrencyConflictException">
    /// si le stock a été modifié concurremment entre la lecture et l'écriture : <b>aucune</b> modification
    /// n'est persistée.
    /// </exception>
    Task<StockAdjustmentResult> AdjustStockToAsync(long productId, int targetQuantity, CancellationToken cancellationToken = default);
}
