namespace MMV.Application.UseCases.Stock.ListStockMovements;

/// <summary>
/// Entrée (query) du use case <see cref="ListStockMovementsUseCase"/> — « Lister les mouvements de stock » (P2D-4).
/// La recherche, le filtre (type / produit) et la pagination restent en présentation
/// (<c>StockMovementsListViewModel</c>, iso-fonctionnel) : la query renvoie la liste complète et ne porte aucun
/// critère.
/// </summary>
public sealed class ListStockMovementsQuery
{
}
