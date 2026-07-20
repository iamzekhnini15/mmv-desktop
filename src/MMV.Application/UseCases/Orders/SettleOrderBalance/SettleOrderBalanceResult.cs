using MMV.Domain.Entities;

namespace MMV.Application.UseCases.Orders.SettleOrderBalance;

/// <summary>
/// Sortie (DTO) du use case <see cref="SettleOrderBalanceUseCase"/>. Permet à la ViewModel de poursuivre son
/// flux (mise à jour de l'état local du paiement, rafraîchissement de la liste/Kanban) sans accéder aux détails
/// de persistance.
/// </summary>
/// <remarks>
/// Lorsque la commande (ou sa vente liée) est introuvable, <see cref="OrderFound"/> vaut <c>false</c> et
/// <see cref="Order"/> est <c>null</c> : la ViewModel affiche alors « Commande introuvable. » exactement comme le
/// flux d'origine, sans modifier son état. En cas de succès, l'entité <see cref="Order"/> fraîchement rechargée
/// (avec sa vente mise à jour) est exposée (transit d'entité toléré pendant la migration, comme
/// <c>AdvanceOrderStatusResult.Order</c>) car la ViewModel doit réafficher la commande et propager l'événement
/// <c>OrderUpdated</c> avec cette entité, à l'identique.
/// </remarks>
public sealed class SettleOrderBalanceResult
{
    /// <summary>Indique si la commande et sa vente liée ont été retrouvées (sinon : « Commande introuvable. »).</summary>
    public bool OrderFound { get; init; }

    /// <summary>Commande fraîchement rechargée (avec sa vente mise à jour), ou <c>null</c> si introuvable.</summary>
    public Order? Order { get; init; }

    /// <summary>Montant du solde encaissé (montant restant avant encaissement), tel que repris dans la notification.</summary>
    public decimal AmountEncashed { get; init; }

    /// <summary>Montant restant à payer avant encaissement.</summary>
    public decimal? OldRemainingAmount { get; init; }

    /// <summary>Montant restant à payer après encaissement (0 dans le flux d'origine).</summary>
    public decimal? NewRemainingAmount { get; init; }

    /// <summary>Vrai si la commande est entièrement payée après encaissement (solde restant nul).</summary>
    public bool IsFullyPaid { get; init; }

    /// <summary>Vrai si une notification d'encaissement a été créée.</summary>
    public bool HasNotification { get; init; }

    /// <summary>
    /// Vrai lorsque le solde était <b>déjà réglé</b> au moment de la prise atomique (P3-7) : rejeu, second clic,
    /// ou règlement concurrent gagné par un autre poste. Aucune écriture, aucun réencaissement et
    /// <b>aucune notification</b> n'ont eu lieu ; <see cref="AmountEncashed"/> vaut alors <c>0</c>.
    /// </summary>
    public bool AlreadySettled { get; init; }
}
