using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.SettleOrderBalance;

/// <summary>
/// Implémentation du use case « Encaisser le solde restant d'une commande » (P2B-2G). Déplace, <b>sans
/// changement de comportement observable</b>, l'orchestration qui vivait dans
/// <c>OrderDetailViewModel.ExecuteEncashBalanceAsync</c> vers la couche Application. Réutilise telles quelles les
/// interfaces de persistance existantes (<see cref="IOrderRepository"/>, <see cref="IUnitOfWork"/>,
/// <see cref="ITransactionRunner"/>, <see cref="INotificationRepository"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue <b>deux</b> écritures successives — un premier
/// <c>SaveChangesAsync</c> pour le paiement de la vente, puis un second pour la notification. Conformément au
/// contrat de migration (R-23 : pas d'écriture multi-étapes sans transaction), ces deux écritures sont désormais
/// regroupées dans une frontière transactionnelle unique ouverte <b>ici</b> (et non plus dans la ViewModel) via
/// <see cref="ITransactionRunner"/>. Le chemin nominal est identique au flux d'origine ; seule la rare voie
/// d'échec partiel est consolidée (si la notification échoue, la mise à jour du paiement est annulée — plus de
/// solde « à moitié encaissé »). Aucune logique de paiement nouvelle n'est introduite : la mise à jour reste
/// strictement <c>DepositAmount = FinalAmount</c> et <c>RemainingAmount = 0</c>, à l'identique.
/// </para>
/// <para>
/// <see cref="INotificationRepository"/> est <b>optionnel</b> (peut être <c>null</c>), reproduisant exactement la
/// garde <c>if (_notificationRepository != null)</c> du flux d'origine. Le montant encaissé, le texte de la
/// notification (au caractère près, format <c>:F2</c> compris) et l'« introuvable » (commande ou vente liée
/// absente) sont préservés tels quels.
/// </para>
/// <para>
/// <b>P3-7 — règlement atomique et idempotent.</b> Le règlement était le <b>seul</b> acte sensible du système
/// dépourvu de protection concurrentielle : deux postes réglant le même solde réussissaient tous les deux et
/// créaient <b>deux</b> notifications du même montant, comptabilisant deux fois un encaissement unique. L'écriture
/// passe désormais par <see cref="ISaleRepository.TrySettleRemainingBalanceAsync"/>, un <c>UPDATE</c> conditionnel
/// atomique : le solde ne peut passer à zéro qu'<b>une seule fois</b>, et la notification n'est créée que si cette
/// prise a réussi. Un second appel — rejeu, double clic hors garde UI, ou poste concurrent — est refusé
/// proprement (<see cref="SettleOrderBalanceResult.AlreadySettled"/>) <b>sans</b> réécriture ni notification.
/// </para>
/// <para>
/// <b>Sémantique inchangée.</b> L'opération règle toujours l'<b>intégralité</b> du solde restant, aucun montant
/// n'est fourni par l'appelant, aucune entité <c>Payment</c> n'est créée, aucun paiement partiel n'est traité et
/// aucun <c>OrderStatus</c> n'est modifié. Seul <see cref="MMV.Domain.Entities.Sale.PaymentStatus"/> passe à
/// <c>Paid</c>, cohérent avec la vérité de paiement établie par P3-7.
/// </para>
/// </remarks>
public sealed class SettleOrderBalanceUseCase : ISettleOrderBalanceUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ISaleRepository _saleRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly INotificationRepository? _notificationRepository;

    public SettleOrderBalanceUseCase(
        IOrderRepository orderRepository,
        ISaleRepository saleRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        INotificationRepository? notificationRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        // Règlement atomique obligatoire (P3-7) : l'écriture du solde passe par une primitive conditionnelle.
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Frontière transactionnelle obligatoire (P2A-1C, R-23) : le flux d'origine fait deux SaveChanges.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        // Notification optionnelle (comme le flux d'origine) : si null, aucune notification n'est créée.
        _notificationRepository = notificationRepository;
    }

    /// <inheritdoc />
    public Task<SettleOrderBalanceResult> ExecuteAsync(SettleOrderBalanceCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Frontière transactionnelle ouverte ICI, dans le use case (plus jamais dans la ViewModel) : la mise à
        // jour du paiement de la vente et la notification sont persistées de façon atomique ; toute exception
        // annule TOUT (aucune écriture partielle).
        return _transactionRunner.RunAsync(ct => SettleAsync(command, ct), cancellationToken);
    }

    /// <summary>
    /// Recharge la commande fraîche avec sa vente, <b>prend</b> le solde de façon atomique conditionnelle, puis
    /// crée éventuellement la notification. Conçue pour s'exécuter dans la frontière transactionnelle : toute
    /// exception levée ici provoque l'annulation complète de l'écriture.
    /// </summary>
    private async Task<SettleOrderBalanceResult> SettleAsync(SettleOrderBalanceCommand command, CancellationToken cancellationToken)
    {
        // Recharger la commande fraîche avec sa vente (iso-fonctionnel).
        var fresh = await _orderRepository.GetWithItemsAsync(command.OrderId, cancellationToken);
        if (fresh is null || fresh.Sale is null)
        {
            // « Commande introuvable. » côté ViewModel : aucun changement d'état, aucune écriture.
            return new SettleOrderBalanceResult { OrderFound = false };
        }

        // Le solde est lu UNIQUEMENT pour libeller la notification après un succès : cette lecture ne décide
        // jamais de l'écriture, qui est tranchée par la condition atomique ci-dessous.
        var amountToEncash = fresh.Sale.RemainingAmount ?? 0m;

        // Prise ATOMIQUE du solde (P3-7) : « il reste quelque chose à encaisser » est évalué par la même
        // instruction que l'écriture. Deux appels concurrents ⇒ un seul succès, un seul passage à zéro.
        var settled = await _saleRepository.TrySettleRemainingBalanceAsync(fresh.Sale.SaleId, cancellationToken);
        if (!settled)
        {
            // Aucune ligne affectée : solde déjà nul, déjà réglé par un autre poste, ou rejeu. Refus métier
            // contrôlé — aucune écriture, AUCUNE notification, aucun montant réencaissé. Aucun message technique
            // EF/SQLite n'est exposé.
            return new SettleOrderBalanceResult
            {
                OrderFound = true,
                Order = fresh,
                AlreadySettled = true,
                AmountEncashed = 0m,
                OldRemainingAmount = 0m,
                NewRemainingAmount = 0m,
                IsFullyPaid = true,
                HasNotification = false
            };
        }

        // Créer une notification (si le repository est disponible), texte préservé au caractère près. Elle n'est
        // atteinte QUE si la prise atomique a réussi : un seul encaissement ⇒ une seule notification.
        var hasNotification = false;
        if (_notificationRepository != null)
        {
            var notification = new Notification
            {
                Type = "PaymentReceived",
                Title = $"Paiement encaissé - {fresh.OrderNumber}",
                Message = $"Le solde de {amountToEncash:F2} € a été encaissé pour la commande {fresh.OrderNumber}.",
                EntityId = fresh.OrderId,
                EntityType = "Order",
                IsRead = false,
                CreatedAt = DateTime.Now
            };
            await _notificationRepository.CreateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            hasNotification = true;
        }

        // Aligner l'entité renvoyée sur ce que la prise atomique vient d'écrire en base. Placé APRÈS le dernier
        // SaveChangesAsync : ces valeurs ne sont donc jamais réécrites par le change tracker — l'UPDATE
        // conditionnel reste l'unique auteur du règlement. Sans cet alignement, la ViewModel réafficherait le
        // solde périmé chargé avant la prise (ExecuteUpdate ne met pas à jour les entités suivies).
        fresh.Sale.DepositAmount = fresh.Sale.FinalAmount;
        fresh.Sale.RemainingAmount = 0m;
        fresh.Sale.PaymentStatus = PaymentStatus.Paid;

        return new SettleOrderBalanceResult
        {
            OrderFound = true,
            Order = fresh,
            AmountEncashed = amountToEncash,
            OldRemainingAmount = amountToEncash,
            NewRemainingAmount = 0m,
            IsFullyPaid = true,
            HasNotification = hasNotification
        };
    }
}
