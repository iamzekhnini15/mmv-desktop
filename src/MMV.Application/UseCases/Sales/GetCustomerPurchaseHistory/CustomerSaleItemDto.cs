using System;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.GetCustomerPurchaseHistory;

/// <summary>
/// DTO applicatif (lecture seule) d'une ligne de l'historique d'achats d'un client (P2D-5). Porte exactement les
/// champs consommés par les <c>DataGrid</c> de <c>CustomerPurchaseHistoryView</c> (date, n° de vente, statut,
/// livraison estimée, montant final, acompte, restant) et le sous-ensemble affiché par <c>CustomerInfoView</c>
/// (date, n° de vente, statut, montant final). Remplace l'entité EF <c>Sale</c> côté UI : aucune navigation, aucun
/// article, aucune entité suivie ne franchit la frontière.
/// </summary>
/// <remarks>
/// <see cref="Status"/> est une enum Domain (valeur métier stable), acceptable en DTO : ce n'est pas une entité.
/// </remarks>
public sealed class CustomerSaleItemDto
{
    /// <summary>Identifiant de la vente.</summary>
    public long SaleId { get; init; }

    /// <summary>Numéro de vente unique (ex. VTE-2026-0001).</summary>
    public string SaleNumber { get; init; } = string.Empty;

    /// <summary>Date de la vente (tri décroissant hérité du repository).</summary>
    public DateTime SaleDate { get; init; }

    /// <summary>Date estimée de livraison, si fabrication nécessaire.</summary>
    public DateTime? EstimatedDelivery { get; init; }

    /// <summary>Montant final après remise.</summary>
    public decimal FinalAmount { get; init; }

    /// <summary>Montant de l'acompte versé, le cas échéant.</summary>
    public decimal? DepositAmount { get; init; }

    /// <summary>Montant restant à payer, le cas échéant.</summary>
    public decimal? RemainingAmount { get; init; }

    /// <summary>Statut de la vente dans le workflow.</summary>
    public SaleStatus Status { get; init; }
}
