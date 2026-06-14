using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Orders.CreateOrder;

/// <summary>
/// Implémentation du use case « Créer une commande fournisseur » (P2B-2D). Déplace, <b>sans changement de
/// comportement observable</b>, l'orchestration de persistance qui vivait dans la branche création de
/// <c>OrderFormViewModel.SaveAsync</c> vers la couche Application. Réutilise tels quels les repositories
/// existants (<see cref="IOrderRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// Le flux d'origine effectue une <b>seule</b> écriture atomique (<c>CreateAsync</c> + un unique
/// <c>SaveChangesAsync</c>) sans <c>ITransactionRunner</c> : il n'y a pas de séquence multi-étapes à protéger,
/// donc aucun runner de transaction n'est introduit (déplacement iso-fonctionnel, pas refonte).
/// </para>
/// <para>
/// La numérotation <c>ORDER</c> (P2A-1E) <b>reste attribuée à l'ouverture du formulaire</b> par la ViewModel et
/// affichée en lecture seule ; le numéro déjà attribué est transmis via
/// <see cref="CreateOrderCommand.OrderNumber"/> et persisté tel quel. Préserver ce comportement évite de
/// modifier l'affichage existant (champ « Numéro » renseigné dès l'ouverture).
/// </para>
/// </remarks>
public sealed class CreateOrderUseCase : ICreateOrderUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrderUseCase(IOrderRepository orderRepository, IUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<CreateOrderResult> ExecuteAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var order = new Order
        {
            OrderNumber = command.OrderNumber,
            EstimatedDelivery = command.EstimatedDelivery,
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes,
            // Branche création du flux d'origine : OrderDate = UtcNow, statut initial New.
            OrderDate = DateTime.UtcNow,
            Status = OrderStatus.New
        };

        // Construire les articles (mêmes champs que le flux d'origine ; paramètres optiques conservés pour les
        // verres uniquement — règle dérivée du type d'article, identique à OrderItemLine.IsLens).
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

        await _orderRepository.CreateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateOrderResult
        {
            OrderId = order.OrderId,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            EstimatedDelivery = order.EstimatedDelivery
        };
    }
}
