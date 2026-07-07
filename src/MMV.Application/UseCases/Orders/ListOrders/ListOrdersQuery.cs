namespace MMV.Application.UseCases.Orders.ListOrders;

/// <summary>
/// Entrée (query) du use case <see cref="ListOrdersUseCase"/> — « Lister les commandes » (P2D-6). Cette lecture n'a
/// pas de critère (la liste et le Kanban chargent l'ensemble des commandes puis filtrent / répartissent en
/// présentation, iso-fonctionnel). L'objet est conservé pour l'homogénéité des query use cases et la garde
/// « query nulle » du use case.
/// </summary>
public sealed class ListOrdersQuery
{
}
