using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
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
/// </remarks>
public sealed class SettleOrderBalanceUseCase : ISettleOrderBalanceUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly INotificationRepository? _notificationRepository;

    public SettleOrderBalanceUseCase(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        INotificationRepository? notificationRepository = null)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
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
    /// Recharge la commande fraîche avec sa vente, encaisse le solde (acompte ← montant final, restant ← 0),
    /// puis crée éventuellement la notification. Conçue pour s'exécuter dans la frontière transactionnelle : toute
    /// exception levée ici provoque l'annulation complète de l'écriture. Port iso-fonctionnel de l'ancien
    /// <c>ExecuteEncashBalanceAsync</c>.
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

        // Sauvegarder le montant restant avant modification pour la notification (sémantique d'origine).
        var amountToEncash = fresh.Sale.RemainingAmount ?? 0m;

        // Mettre à jour le paiement sur la vente : acompte devient le montant total, restant devient 0.
        fresh.Sale.DepositAmount = fresh.Sale.FinalAmount;
        fresh.Sale.RemainingAmount = 0m;

        await _orderRepository.UpdateAsync(fresh, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Créer une notification (si le repository est disponible), texte préservé au caractère près.
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
