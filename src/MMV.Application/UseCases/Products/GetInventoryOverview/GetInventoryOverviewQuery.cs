namespace MMV.Application.UseCases.Products.GetInventoryOverview;

/// <summary>
/// Entrée (query) du use case <see cref="GetInventoryOverviewUseCase"/> — « Obtenir l'état d'inventaire » (P2D-4).
/// Alimente l'écran d'inventaire physique (<c>InventoryViewModel</c>) avec la liste des produits et leur stock
/// théorique. La recherche reste en présentation : la query renvoie la liste complète, triée par nom.
/// </summary>
public sealed class GetInventoryOverviewQuery
{
}
