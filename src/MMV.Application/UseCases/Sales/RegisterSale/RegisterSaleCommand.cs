using System.Collections.Generic;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Entrée (DTO) du use case <see cref="RegisterSaleUseCase"/> — « Enregistrer une vente en magasin ».
/// Contient uniquement les données nécessaires provenant de <c>SaleFormViewModel</c> ; la ViewModel
/// transforme son état (champs liés à l'UI) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Les montants (<see cref="TotalAmount"/>,
/// <see cref="FinalAmount"/>, <see cref="RemainingAmount"/>…) sont calculés par la ViewModel comme dans le
/// flux d'origine (<c>CalculateFinalAmount</c>) et transmis tels quels : le use case ne recalcule pas, il
/// déplace la logique de persistance sans en changer le comportement observable. Aucun nouveau concept
/// métier, aucun <c>Money</c>, aucune devise.
/// </remarks>
public sealed class RegisterSaleCommand
{
    /// <summary>Identifiant du client de la vente.</summary>
    public long CustomerId { get; init; }

    /// <summary>
    /// Vente comptoir immédiate (<c>true</c> : livrée + décrément de stock hors verres) ou vente avec
    /// fabrication (<c>false</c> : en attente verres). Reproduit <c>SaleFormViewModel.IsCounterSale</c>.
    /// </summary>
    public bool IsCounterSale { get; init; }

    /// <summary>Montant total avant remise (calculé par la ViewModel).</summary>
    public decimal TotalAmount { get; init; }

    /// <summary>Montant de la remise appliquée.</summary>
    public decimal DiscountAmount { get; init; }

    /// <summary>Montant final après remise (calculé par la ViewModel).</summary>
    public decimal FinalAmount { get; init; }

    /// <summary>Montant de l'acompte versé.</summary>
    public decimal DepositAmount { get; init; }

    /// <summary>Montant restant à payer (calculé par la ViewModel).</summary>
    public decimal RemainingAmount { get; init; }

    /// <summary>Méthode de paiement (déjà convertie depuis le libellé UI par la ViewModel).</summary>
    public PaymentMethod PaymentMethod { get; init; }

    /// <summary>Notes libres de la vente.</summary>
    public string? Notes { get; init; }

    /// <summary>Lignes du panier (articles, paramètres optiques OD/OG inclus).</summary>
    public IReadOnlyList<RegisterSaleLineCommand> Lines { get; init; } = new List<RegisterSaleLineCommand>();
}
