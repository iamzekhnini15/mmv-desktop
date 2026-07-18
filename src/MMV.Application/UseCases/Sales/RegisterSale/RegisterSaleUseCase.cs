using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Implémentation du use case « Enregistrer une vente en magasin » (P2B-2C). Déplace, <b>sans changement de
/// comportement observable</b>, l'orchestration métier qui vivait dans
/// <c>SaleFormViewModel.PersistSaleAsync</c> vers la couche Application. Réutilise telles quelles les
/// primitives P2A (<see cref="ITransactionRunner"/>, <see cref="INumberSequenceService"/>,
/// <see cref="IStockMutationService"/>) et les repositories existants.
/// </summary>
/// <remarks>
/// La frontière transactionnelle est désormais ouverte <b>ici</b> (et non plus dans la ViewModel) : la
/// numérotation, la création de la vente / commande fournisseur et les mouvements de stock participent à la
/// même transaction (commit si succès, rollback complet si exception — aucune écriture partielle).
/// </remarks>
public sealed class RegisterSaleUseCase : IRegisterSaleUseCase
{
    private readonly ISaleRepository _saleRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly IStockMutationService _stockMutationService;
    private readonly INumberSequenceService _numberSequenceService;

    public RegisterSaleUseCase(
        ISaleRepository saleRepository,
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        IStockMovementRepository stockMovementRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        IStockMutationService stockMutationService,
        INumberSequenceService numberSequenceService)
    {
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Frontière transactionnelle obligatoire (P2A-1C, R-23) : pas d'écriture multi-étapes sans transaction.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        // Décrément de stock sûr obligatoire (P2A-1D, R-09).
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
        // Numérotation fiable obligatoire (P2A-1E, R-03).
        _numberSequenceService = numberSequenceService ?? throw new ArgumentNullException(nameof(numberSequenceService));
    }

    /// <inheritdoc />
    public Task<RegisterSaleResult> ExecuteAsync(RegisterSaleCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Frontière transactionnelle OBLIGATOIRE (P2A-1C, R-23) : ouverte ICI, dans le use case (plus jamais
        // dans la ViewModel). La création de la vente, l'éventuelle commande fournisseur et les mouvements de
        // stock sont persistés de façon atomique ; toute exception annule TOUT (aucune écriture partielle).
        return _transactionRunner.RunAsync(ct => PersistSaleAsync(command, ct), cancellationToken);
    }

    /// <summary>
    /// Construit la vente puis la persiste (vente → éventuelle commande fournisseur → mouvements de stock).
    /// Conçue pour s'exécuter dans la frontière transactionnelle : toute exception levée ici provoque
    /// l'annulation complète de l'écriture. Port iso-fonctionnel de l'ancien <c>PersistSaleAsync</c>.
    /// </summary>
    private async Task<RegisterSaleResult> PersistSaleAsync(RegisterSaleCommand command, CancellationToken cancellationToken)
    {
        // Numéro de vente fiable (P2A-1E, R-03) : séquence déterministe unique attribuée DANS la transaction ;
        // un numéro attribué pour une vente annulée n'est pas consommé.
        var saleNumber = await _numberSequenceService.NextNumberAsync(DocumentSequenceNames.Sale, cancellationToken);

        // Créer la vente complète
        var sale = new Sale
        {
            CustomerId = command.CustomerId,
            SaleDate = DateTime.Now,
            SaleNumber = saleNumber,
            TotalAmount = command.TotalAmount,
            DiscountAmount = command.DiscountAmount,
            FinalAmount = command.FinalAmount,
            DepositAmount = command.DepositAmount,
            RemainingAmount = command.RemainingAmount,
            PaymentMethod = command.PaymentMethod,
            Notes = command.Notes
        };

        // Configuration selon le type de vente
        if (command.IsCounterSale)
        {
            // Vente comptoir immédiate : livrée directement
            sale.Status = SaleStatus.Delivered;
            sale.EstimatedDelivery = DateTime.Now;
        }
        else
        {
            // Vente avec fabrication : attente verres
            sale.Status = SaleStatus.AwaitingLenses;
            sale.EstimatedDelivery = DateTime.Now.AddDays(14);
        }

        // Ajouter les articles avec tous leurs paramètres
        foreach (var line in command.Lines)
        {
            sale.SaleItems.Add(new SaleItem
            {
                ProductId = line.ProductId,
                ItemType = line.ItemType == OrderItemType.Frame ? OrderItemType.Frame :
                          line.ItemType == OrderItemType.LensOd ? OrderItemType.LensOd :
                          line.ItemType == OrderItemType.LensOg ? OrderItemType.LensOg :
                          OrderItemType.Accessory,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                // Paramètres de correction (pour verres)
                UsageType = line.UsageType,
                Sphere = line.Sphere,
                Cylinder = line.Cylinder,
                Axis = line.Axis,
                Addition = line.Addition,
                PrismValue = line.PrismValue,
                PrismBase = line.PrismBase,
                VisualAcuity = line.VisualAcuity
            });
        }

        Order? createdOrder = null;
        string? orderNumber = null;

        // Sauvegarder la vente (obtenir SaleId)
        await _saleRepository.CreateAsync(sale, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Si la vente contient des verres, créer automatiquement une commande fournisseur
        var hasLenses = command.Lines.Any(i => i.ItemType == OrderItemType.LensOd || i.ItemType == OrderItemType.LensOg);
        if (hasLenses)
        {
            // Numéro de commande fournisseur fiable (P2A-1E, R-03), même transaction que la vente.
            orderNumber = await _numberSequenceService.NextNumberAsync(DocumentSequenceNames.Order, cancellationToken);

            var order = new Order
            {
                SaleId = sale.SaleId,
                OrderNumber = orderNumber,
                OrderDate = DateTime.Now,
                EstimatedDelivery = DateTime.Now.AddDays(14),
                Status = OrderStatus.New,
                Notes = $"Commande verres pour vente {sale.SaleNumber}"
            };

            // Ajouter uniquement les verres à la commande fournisseur
            foreach (var line in command.Lines.Where(i => i.ItemType == OrderItemType.LensOd || i.ItemType == OrderItemType.LensOg))
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = line.ProductId,
                    ItemType = line.ItemType,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UsageType = line.UsageType,
                    Sphere = line.Sphere,
                    Cylinder = line.Cylinder,
                    Axis = line.Axis,
                    Addition = line.Addition,
                    PrismValue = line.PrismValue,
                    PrismBase = line.PrismBase,
                    VisualAcuity = line.VisualAcuity
                });
            }

            await _orderRepository.CreateAsync(order, cancellationToken);
            createdOrder = order;
        }

        // Décrément du stock des produits NON-VERRE (montures, clips, accessoires, solaires…) à l'enregistrement de
        // la vente, que la vente soit comptoir OU fabrication (P3-5). Les VERRES / LENTILLES sont commandés aux
        // fournisseurs (mis dans l'Order ci-dessus) et décrémentés plus tard, au passage en fabrication
        // (AdvanceOrderStatusUseCase). Un non-verre n'entrant jamais dans l'Order, il n'est décrémenté qu'une fois ici.
        foreach (var line in command.Lines)
        {
            // Vérifier que le ProductId existe
            if (!line.ProductId.HasValue)
                continue;

            // Charger le produit pour connaître sa catégorie
            var product = await _productRepository.GetByIdAsync(line.ProductId.Value, cancellationToken);
            if (product != null)
            {
                // Exclure les verres/lentilles de la décrémentation à la vente (décrémentés à la fabrication).
                bool isLens = product.Category == ProductCategoryEnum.VERRE || product.Category == ProductCategoryEnum.LENTILLE;
                if (isLens)
                    continue;

                // Décrément atomique conditionnel (P2A-1D, R-09) : ne rend jamais le stock négatif et élimine la
                // mise à jour perdue. En cas de stock insuffisant (ou modifié entre-temps), une
                // InsufficientStockException est levée → le runner annule TOUTE la vente.
                await _stockMutationService.DecrementStockAsync(line.ProductId.Value, line.Quantity, cancellationToken);

                // Créer le mouvement de stock (sortie) : Quantity négative (convention de signe P3-5).
                var stockMovement = new StockMovement
                {
                    ProductId = line.ProductId.Value,
                    MovementType = StockMovementType.Out,
                    Quantity = -line.Quantity, // Négatif pour sortie
                    Reason = $"Vente {sale.SaleNumber} - Client #{command.CustomerId}",
                    CreatedAt = DateTime.Now
                };
                await _stockMovementRepository.CreateAsync(stockMovement, cancellationToken);
            }
        }

        // Sauvegarder toutes les modifications (commande + mouvements de stock)
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterSaleResult
        {
            SaleId = sale.SaleId,
            SaleNumber = sale.SaleNumber,
            OrderId = createdOrder?.OrderId,
            OrderNumber = orderNumber,
            Status = sale.Status,
            FinalAmount = sale.FinalAmount,
            RemainingAmount = sale.RemainingAmount ?? 0m,
            Sale = sale
        };
    }
}
