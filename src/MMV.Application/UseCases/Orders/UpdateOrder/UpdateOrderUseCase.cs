using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.UpdateOrder;

/// <summary>
/// Implémentation du use case « Modifier une commande existante » (P2B-2I). Déplace, <b>sans changement de
/// comportement observable</b>, l'orchestration de persistance qui vivait dans la branche édition de
/// <c>OrderFormViewModel.SaveAsync</c> (méthode <c>UpdateExistingOrderAsync</c>) vers la couche Application.
/// Réutilise telles quelles les interfaces de persistance existantes (<see cref="IOrderRepository"/>,
/// <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue une <b>seule</b> écriture atomique
/// (<c>UpdateAsync</c> + un unique <c>SaveChangesAsync</c>) sans <c>ITransactionRunner</c> : il n'y a pas de
/// séquence multi-étapes à protéger (la suppression et le ré-ajout des lignes font partie du même
/// <c>SaveChangesAsync</c> via les cascades EF existantes), donc aucun runner de transaction n'est introduit
/// (déplacement iso-fonctionnel, pas refonte).
/// </para>
/// <para>
/// <b>Chargement.</b> La commande est rechargée <i>avec ses articles</i> (<see cref="IOrderRepository.GetWithItemsAsync"/>)
/// afin que la suppression des lignes existantes (<c>OrderItems.Clear()</c>) puis leur reconstruction soient
/// suivies par EF exactement comme dans le flux d'origine (où l'entité <c>_existingOrder</c> était déjà chargée
/// avec ses lignes par la ViewModel).
/// </para>
/// <para>
/// <b>Comportement introuvable.</b> En mode édition, la ViewModel ouvre toujours le formulaire avec une commande
/// réelle ; le cas « introuvable » ne survient donc pas en pratique. Le flux d'origine couvrait ce cas théorique
/// par <c>_existingOrder ?? new Order()</c> (création d'une commande vierge), un chemin mort jamais atteint. Ce
/// use case adopte un contrat plus sûr et explicite : si la commande n'existe pas, il renvoie
/// <see cref="UpdateOrderResult.OrderFound"/> = <c>false</c> sans écrire (cohérent avec
/// <c>DeleteOrderUseCase</c>), plutôt que de persister une commande fantôme.
/// </para>
/// <para>
/// <b>Statut / numérotation.</b> L'édition ne touche ni le statut ni la numérotation (comme le flux d'origine) :
/// le numéro est réaffecté à l'identique et le statut courant est préservé puis renvoyé dans le résultat.
/// </para>
/// </remarks>
public sealed class UpdateOrderUseCase : IUpdateOrderUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateOrderUseCase(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<UpdateOrderResult> ExecuteAsync(UpdateOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Charger la commande avec ses lignes (nécessaire pour la reconstruction Clear + ré-ajout, suivie par EF).
        var order = await _orderRepository.GetWithItemsAsync(command.OrderId, cancellationToken);
        if (order is null)
            return new UpdateOrderResult { OrderFound = false, OrderId = command.OrderId };

        // Garde métier P3-6 : la modification (qui reconstruit intégralement les lignes) n'est autorisée que tant
        // que la fabrication n'a pas commencé — c.-à-d. New ou ToFabricate. Dès InProgress, le stock a pu être
        // décrémenté sur les lignes existantes ; vider puis recréer les lignes désynchroniserait la consommation de
        // stock déjà enregistrée, sans mouvement compensatoire. On refuse donc toute édition à partir de InProgress.
        if (order.Status != OrderStatus.New && order.Status != OrderStatus.ToFabricate)
            throw new BusinessRuleException("Cette commande ne peut plus être modifiée après le début de la fabrication.");

        // Mise à jour des champs éditables (mêmes champs que le flux d'origine : numéro réaffecté à l'identique,
        // date estimée, notes normalisées en null si blanches). Statut/SaleId inchangés.
        order.OrderNumber = command.OrderNumber;
        order.EstimatedDelivery = command.EstimatedDelivery;
        order.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;

        // Reconstruire les lignes : vider l'existant puis ré-ajouter (stratégie identique au flux d'origine ;
        // les paramètres optiques sont conservés pour les verres uniquement — règle dérivée du type d'article,
        // identique à OrderItemLine.IsLens).
        order.OrderItems.Clear();
        foreach (var line in command.Lines)
        {
            bool isLens = line.ItemType == OrderItemType.LensOd || line.ItemType == OrderItemType.LensOg;
            order.OrderItems.Add(new OrderItem
            {
                ProductId = line.ProductId,
                ItemType = line.ItemType,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Sphere = isLens ? line.Sphere : null,
                Cylinder = isLens ? line.Cylinder : null,
                Axis = isLens ? line.Axis : null,
                Addition = isLens ? line.Addition : null
            });
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateOrderResult
        {
            OrderFound = true,
            OrderId = order.OrderId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            EstimatedDelivery = order.EstimatedDelivery
        };
    }
}
