using System.Collections.Generic;
using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Entrée (DTO) du use case <see cref="RegisterSaleUseCase"/> — « Enregistrer une vente en magasin ».
/// Contient uniquement les données nécessaires provenant de <c>SaleFormViewModel</c> ; la ViewModel
/// transforme son état (champs liés à l'UI) en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// <para>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Aucun nouveau concept métier, aucun <c>Money</c>, aucune devise.
/// </para>
/// <para>
/// <b>P3-7 — les montants totaux ne sont plus une entrée.</b> <see cref="TotalAmount"/>,
/// <see cref="FinalAmount"/> et <see cref="RemainingAmount"/> sont désormais <b>intégralement recalculés</b> par
/// <c>SalePricingPolicy</c> à partir des lignes : le backend ne leur fait plus confiance. Seuls
/// <see cref="DiscountAmount"/> et <see cref="DepositAmount"/> restent de véritables entrées métier (validées et
/// bornées). Les trois champs hérités sont conservés uniquement pour ne pas casser l'appelant UI existant.
/// </para>
/// </remarks>
public sealed class RegisterSaleCommand
{
    /// <summary>
    /// Identifiant du client de la vente, ou <c>null</c> pour une <b>vente sans client</b> (P3-7).
    /// </summary>
    /// <remarks>
    /// Aligné sur <c>Sale.CustomerId</c>, nullable depuis toujours : le modèle autorisait la vente au comptoir
    /// anonyme, que ce contrat rendait pourtant impossible. Lorsqu'un client est fourni, il doit exister et ne pas
    /// être archivé — vérifié par une prise atomique dans la transaction de la vente.
    /// </remarks>
    public long? CustomerId { get; init; }

    /// <summary>
    /// Vente comptoir immédiate (<c>true</c> : livrée + décrément de stock hors verres) ou vente avec
    /// fabrication (<c>false</c> : en attente verres). Reproduit <c>SaleFormViewModel.IsCounterSale</c>.
    /// </summary>
    public bool IsCounterSale { get; init; }

    /// <summary>
    /// <b>Champ hérité de l'ancien contrat — IGNORÉ par le backend (P3-7).</b> Le montant total est recalculé
    /// depuis les lignes (<c>Σ Quantité × PrixUnitaire</c>) ; la valeur fournie ici n'est ni lue, ni comparée, ni
    /// persistée. Conservé uniquement pour la compatibilité source de l'appelant UI existant.
    /// </summary>
    public decimal TotalAmount { get; init; }

    /// <summary>
    /// Montant de la remise globale appliquée. <b>Véritable entrée métier</b> : validée (≥ 0) et bornée au montant
    /// total recalculé.
    /// </summary>
    public decimal DiscountAmount { get; init; }

    /// <summary>
    /// <b>Champ hérité de l'ancien contrat — IGNORÉ par le backend (P3-7).</b> Le montant final est recalculé
    /// (<c>Total − Remise</c>) ; la valeur fournie ici n'est ni lue, ni comparée, ni persistée.
    /// </summary>
    public decimal FinalAmount { get; init; }

    /// <summary>
    /// Montant de l'acompte versé. <b>Véritable entrée métier</b> : validé (≥ 0) et borné au montant final
    /// recalculé.
    /// </summary>
    public decimal DepositAmount { get; init; }

    /// <summary>
    /// <b>Champ hérité de l'ancien contrat — IGNORÉ par le backend (P3-7).</b> Le solde restant est recalculé
    /// (<c>Final − Acompte</c>) ; la valeur fournie ici n'est ni lue, ni comparée, ni persistée.
    /// </summary>
    public decimal RemainingAmount { get; init; }

    /// <summary>Méthode de paiement (déjà convertie depuis le libellé UI par la ViewModel).</summary>
    public PaymentMethod PaymentMethod { get; init; }

    /// <summary>Notes libres de la vente.</summary>
    public string? Notes { get; init; }

    /// <summary>Lignes du panier (articles, paramètres optiques OD/OG inclus).</summary>
    public IReadOnlyList<RegisterSaleLineCommand> Lines { get; init; } = new List<RegisterSaleLineCommand>();
}
