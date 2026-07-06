namespace MMV.Application.UseCases.Orders.ListOrders;

/// <summary>
/// DTO applicatif (lecture seule) du client d'une commande, tel que consommé par la liste des commandes
/// (<c>OrdersListViewModel</c>) et la vue Kanban (<c>OrderKanbanViewModel</c>) — P2D-6. Ne porte que le
/// prénom et le nom affichés (chemin de liaison <c>Sale.Customer.FirstName/LastName</c> préservé, iso-fonctionnel).
/// Aucune navigation EF <c>Customer</c> n'est exposée.
/// </summary>
public sealed class OrderCustomerSummaryDto
{
    /// <summary>Prénom du client (affichage carte / ligne de commande).</summary>
    public string? FirstName { get; init; }

    /// <summary>Nom du client (affichage carte / ligne de commande).</summary>
    public string? LastName { get; init; }
}
