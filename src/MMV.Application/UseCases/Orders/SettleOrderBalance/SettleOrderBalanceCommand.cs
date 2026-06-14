namespace MMV.Application.UseCases.Orders.SettleOrderBalance;

/// <summary>
/// Entrée (DTO) du use case <see cref="SettleOrderBalanceUseCase"/> — « Encaisser le solde restant d'une
/// commande » (cinquième vertical slice, P2B-2G). Contient uniquement la donnée nécessaire provenant de
/// <c>OrderDetailViewModel</c> : l'identifiant de la commande dont le solde est encaissé.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de
/// <c>OrderDetailViewModel.ExecuteEncashBalanceAsync</c> : le flux d'origine recharge la commande fraîche par son
/// identifiant puis lit lui-même, sur l'entité rechargée, le montant restant, le montant final et le numéro de
/// commande utilisés pour la mise à jour et la notification. Aucune autre donnée n'a donc à transiter par la
/// commande, et aucun nouveau concept métier n'est introduit (aucun <c>Money</c>, aucune devise, aucune règle
/// pays/fiscalité/facture/devis).
/// </remarks>
public sealed class SettleOrderBalanceCommand
{
    /// <summary>Identifiant de la commande dont le solde restant est encaissé.</summary>
    public long OrderId { get; init; }
}
