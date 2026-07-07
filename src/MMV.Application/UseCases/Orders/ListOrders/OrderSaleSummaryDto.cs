namespace MMV.Application.UseCases.Orders.ListOrders;

/// <summary>
/// DTO applicatif (lecture seule) de la vente rattachée à une commande, tel que consommé par la liste des commandes
/// (<c>OrdersListViewModel</c>) et la vue Kanban (<c>OrderKanbanViewModel</c>) — P2D-6. Porte les montants affichés
/// et le client, en préservant les chemins de liaison existants (<c>Sale.FinalAmount</c>, <c>Sale.DepositAmount</c>,
/// <c>Sale.RemainingAmount</c>, <c>Sale.Customer.*</c>) pour rester strictement iso-fonctionnel. Les types sont
/// identiques à ceux de l'entité <c>Sale</c> (<c>decimal</c> / <c>decimal?</c>) afin de conserver le formatage
/// d'affichage (<c>StringFormat</c>, <c>TargetNullValue</c>). Aucune navigation EF <c>Sale</c> n'est exposée.
/// </summary>
public sealed class OrderSaleSummaryDto
{
    /// <summary>Montant final de la vente.</summary>
    public decimal FinalAmount { get; init; }

    /// <summary>Acompte versé (peut être absent → affiché « - »).</summary>
    public decimal? DepositAmount { get; init; }

    /// <summary>Reste à payer (peut être absent → affiché « - »).</summary>
    public decimal? RemainingAmount { get; init; }

    /// <summary>Client de la vente (prénom / nom affichés), ou <c>null</c>.</summary>
    public OrderCustomerSummaryDto? Customer { get; init; }
}
