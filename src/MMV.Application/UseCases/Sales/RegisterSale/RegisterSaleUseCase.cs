using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.UseCases.Prescriptions.CreatePrescription;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Interfaces.Time;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.Sales.RegisterSale;

/// <summary>
/// Implémentation du use case « Enregistrer une vente en magasin » (P2B-2C), <b>sécurisée en P3-7</b>. Réutilise
/// telles quelles les primitives P2A (<see cref="ITransactionRunner"/>, <see cref="INumberSequenceService"/>,
/// <see cref="IStockMutationService"/>) et la politique de stock P3-5, auxquelles P3-7 ajoute les règles métier
/// qui manquaient : lignes validées, montants recalculés, client et produits réellement vérifiés.
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle inchangée.</b> Un unique <see cref="ITransactionRunner.RunAsync"/> englobe
/// toujours numérotation, vente, commande fournisseur et mouvements de stock (P2A-1C, R-23). P3-7 n'a rien
/// fragmenté : les nouvelles vérifications s'insèrent <b>dans</b> cette frontière, jamais à l'extérieur.
/// </para>
/// <para>
/// <b>Ordre d'exécution (P3-7).</b> Valider et calculer (pur, hors dépôt) ⇒ ouvrir la transaction ⇒ prendre le
/// client actif ⇒ prendre les produits actifs distincts ⇒ numéroter ⇒ écrire. Une commande invalide est donc
/// refusée <b>avant</b> qu'un numéro de vente ne soit même demandé.
/// </para>
/// <para>
/// <b>Le backend est la source de vérité monétaire.</b> <c>command.TotalAmount</c>, <c>command.FinalAmount</c> et
/// <c>command.RemainingAmount</c> ne sont ni lus, ni comparés : <see cref="SalePricingPolicy"/> recalcule tout
/// depuis les lignes. Aucune tolérance de comparaison n'est donc nécessaire vis-à-vis du stockage SQLite
/// <c>REAL</c> (audit §8.1) — écraser plutôt que comparer supprime la question. <c>SaleItem.TotalPrice</c>, jamais
/// affecté avant P3-7 (donc persisté à <c>0</c> sur toutes les ventes existantes), est désormais calculé.
/// </para>
/// <para>
/// <b>Prix négocié conservé.</b> <c>UnitPrice</c> reste celui fourni par l'appelant, jamais
/// <c>Product.SalePrice</c> : remises, promotions et prix négociés sont légitimes. La seule borne opposée est
/// « ≥ 0 » (la gratuité reste permise).
/// </para>
/// <para>
/// <b><see cref="Sale.Status"/> est un indicateur historique initial, pas une vérité de workflow.</b> P3-7 corrige
/// uniquement son état initial contradictoire (une vente contenant des verres ne peut pas naître « livrée ») et
/// n'introduit <b>aucune</b> machine à états ni synchronisation ultérieure. La vérité de fabrication et de
/// livraison reste <c>OrderStatus</c> (P3-6) ; la vérité de paiement devient
/// <see cref="Sale.PaymentStatus"/> (P3-7). Les valeurs <c>Draft</c>, <c>InFabrication</c>, <c>Ready</c> et
/// <c>Cancelled</c> restent inatteignables : dette de modèle documentée, sans nouvelle règle P3-7.
/// </para>
/// </remarks>
public sealed class RegisterSaleUseCase : IRegisterSaleUseCase
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'une ligne de vente ne référence aucun produit. Une ligne sans produit
    /// réel était auparavant persistée en silence, sans aucune mutation de stock : un écart d'inventaire invisible.
    /// </summary>
    public const string ProductRequiredMessage =
        "Chaque ligne de vente doit référencer un produit.";

    /// <summary>
    /// Message métier stable renvoyé lorsqu'un produit référencé par une ligne n'existe pas. Remplace le
    /// <c>continue</c> silencieux d'avant P3-7 (audit §22 C8).
    /// </summary>
    public const string ProductNotFoundMessage =
        "Un produit de cette vente est introuvable. Aucune modification n'a été conservée.";

    /// <summary>
    /// Message métier stable renvoyé lorsqu'un produit désactivé est vendu — y compris désactivé <b>concurremment</b>
    /// depuis l'affichage du panier. Ferme le contournement de <c>Product.IsActive</c> par identifiant direct
    /// (audit §22 I4).
    /// </summary>
    public const string ProductInactiveMessage =
        "Ce produit est désactivé et ne peut plus être vendu.";

    /// <summary>
    /// Message métier stable renvoyé lorsqu'une ligne est classée autrement que le produit qu'elle référence
    /// (P3-11). Réutilise <b>littéralement</b> la constante de <see cref="SaleLineStockFlowPolicy"/>, propriétaire
    /// unique de la classification : la vente et la fabrication opposent donc le <b>même</b> texte.
    /// </summary>
    public const string SaleLineProductCategoryMismatchMessage =
        SaleLineStockFlowPolicy.SaleLineProductCategoryMismatchMessage;

    /// <summary>Message métier stable renvoyé lorsque le client indiqué n'existe pas.</summary>
    public const string CustomerNotFoundMessage =
        "Le client de cette vente est introuvable. Aucune modification n'a été conservée.";

    /// <summary>
    /// Message métier stable renvoyé lorsqu'une vente est enregistrée pour un client archivé. Réutilise
    /// <b>littéralement</b> la constante de <see cref="CreatePrescriptionUseCase"/> (P3-3B) : une même situation
    /// métier — « ce client est archivé, réactivez-le » — produit un message identique quel que soit le flux, et
    /// l'UI n'a qu'un seul texte à reconnaître.
    /// </summary>
    public const string CustomerArchivedMessage = CreatePrescriptionUseCase.CustomerArchivedMessage;

    private readonly ISaleRepository _saleRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITransactionRunner _transactionRunner;
    private readonly IStockMutationService _stockMutationService;
    private readonly INumberSequenceService _numberSequenceService;

    // P4-5D : horloge injectée (ADR-PROD-DB-004 §5, décision 2). Ce use case portait à lui seul CINQ des
    // dix-sept DateTime.Now du dépôt — date de vente, échéance de livraison, date et échéance de la commande
    // fournisseur, horodatage des mouvements de stock.
    private readonly IClock _clock;

    public RegisterSaleUseCase(
        ISaleRepository saleRepository,
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ICustomerRepository customerRepository,
        IStockMovementRepository stockMovementRepository,
        IUnitOfWork unitOfWork,
        ITransactionRunner transactionRunner,
        IStockMutationService stockMutationService,
        INumberSequenceService numberSequenceService,
        IClock clock)
    {
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        // Client vérifié obligatoire (P3-7) : une vente ne peut plus être enregistrée pour un client archivé.
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _stockMovementRepository = stockMovementRepository ?? throw new ArgumentNullException(nameof(stockMovementRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        // Frontière transactionnelle obligatoire (P2A-1C, R-23) : pas d'écriture multi-étapes sans transaction.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
        // Décrément de stock sûr obligatoire (P2A-1D, R-09).
        _stockMutationService = stockMutationService ?? throw new ArgumentNullException(nameof(stockMutationService));
        // Numérotation fiable obligatoire (P2A-1E, R-03).
        _numberSequenceService = numberSequenceService ?? throw new ArgumentNullException(nameof(numberSequenceService));
        // Horloge obligatoire (P4-5D) : une vente est l'événement métier horodaté central du logiciel ; sa
        // chronologie doit rester exacte dans une base partagée par plusieurs postes (§2.3).
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public Task<RegisterSaleResult> ExecuteAsync(RegisterSaleCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // --- Étape 0 (P3-7) : validation PURE et calcul monétaire, AVANT toute transaction et toute numérotation.
        // Une commande invalide ne consomme donc aucun numéro de vente et ne touche jamais le dépôt.
        // Chaque règle est évaluée UNE FOIS, par son propriétaire : présence du produit ici, quantité / prix /
        // remise / acompte par SalePricingPolicy, invariants optiques par SaleLineOpticsPolicy.
        RequireProductOnEveryLine(command);

        var pricing = SalePricingPolicy.Calculate(
            command.Lines.Select(l => new SaleLinePricingInput(l.Quantity, l.UnitPrice)).ToList(),
            command.DiscountAmount,
            command.DepositAmount);

        var validatedLines = ValidateLines(command);

        // Frontière transactionnelle OBLIGATOIRE (P2A-1C, R-23) : ouverte ICI, dans le use case. La prise du
        // client, celle des produits, la création de la vente, l'éventuelle commande fournisseur et les mouvements
        // de stock sont persistés de façon atomique ; toute exception annule TOUT (aucune écriture partielle).
        return _transactionRunner.RunAsync(ct => PersistSaleAsync(command, validatedLines, pricing, ct), cancellationToken);
    }

    /// <summary>
    /// Exige que chaque ligne référence un produit. Une collection nulle est traitée exactement comme une
    /// collection vide : c'est la même règle métier (« une vente doit contenir au moins une ligne »), et non une
    /// erreur d'appel technique.
    /// </summary>
    private static void RequireProductOnEveryLine(RegisterSaleCommand command)
    {
        if (command.Lines is null || command.Lines.Count == 0)
            throw new BusinessRuleException(SalePricingPolicy.EmptyLinesMessage);

        // Chaque ligne doit représenter un produit RÉEL : sans identifiant, aucun stock ne pourrait être
        // mouvementé et la ligne serait un montant sans contrepartie matérielle.
        if (command.Lines.Any(l => !l.ProductId.HasValue))
            throw new BusinessRuleException(ProductRequiredMessage);
    }

    /// <summary>
    /// Produit les lignes validées — hors dépôt, sans aucun accès à la base — avec leurs données optiques sous
    /// forme canonique. Toute violation lève une <see cref="BusinessRuleException"/> : plus aucune ligne invalide
    /// n'est persistée en silence.
    /// </summary>
    private static IReadOnlyList<ValidatedSaleLine> ValidateLines(RegisterSaleCommand command)
    {
        var validated = new List<ValidatedSaleLine>(command.Lines.Count);

        foreach (var line in command.Lines)
        {
            var itemType = NormalizeItemType(line.ItemType);

            var optics = SaleLineOpticsPolicy.ValidateAndNormalize(
                itemType,
                new SaleLineOptics(line.Sphere, line.Cylinder, line.Axis, line.Addition, line.PrismValue, line.PrismBase));

            // Déjà garanti par RequireProductOnEveryLine ; réexprimé ici pour que l'invariant « une ligne validée
            // porte toujours un produit » soit vérifiable localement, y compris par le compilateur.
            var productId = line.ProductId ?? throw new BusinessRuleException(ProductRequiredMessage);

            validated.Add(new ValidatedSaleLine(productId, itemType, line.Quantity, line.UnitPrice, line.UsageType, optics, line.VisualAcuity));
        }

        return validated;
    }

    /// <summary>Normalisation du type d'article, identique au flux d'origine (Frame / LensOd / LensOg, sinon Accessory).</summary>
    private static OrderItemType NormalizeItemType(OrderItemType itemType) => itemType switch
    {
        OrderItemType.Frame => OrderItemType.Frame,
        OrderItemType.LensOd => OrderItemType.LensOd,
        OrderItemType.LensOg => OrderItemType.LensOg,
        _ => OrderItemType.Accessory
    };

    /// <summary>
    /// Prend le client et les produits, puis persiste la vente (vente → éventuelle commande fournisseur →
    /// mouvements de stock). Conçue pour s'exécuter dans la frontière transactionnelle : toute exception levée ici
    /// provoque l'annulation complète de l'écriture.
    /// </summary>
    private async Task<RegisterSaleResult> PersistSaleAsync(
        RegisterSaleCommand command,
        IReadOnlyList<ValidatedSaleLine> lines,
        SalePricing pricing,
        CancellationToken cancellationToken)
    {
        // P4-5D : UN SEUL instant pour toute la vente. Les cinq horodatages produits ici (vente, échéance,
        // commande fournisseur, échéance fournisseur, mouvements de stock) décrivent un seul acte de gestion,
        // écrit dans une seule transaction : cinq lectures d'horloge en auraient fait cinq événements
        // légèrement distincts, et auraient rendu tout test daté dépendant de sa propre durée d'exécution.
        var now = _clock.UtcNow;

        // --- 1) Client facultatif, mais ACTIF lorsqu'il est fourni : prise atomique DANS la transaction.
        await AcquireCustomerAsync(command.CustomerId, cancellationToken);

        // --- 2) Produits obligatoires, existants et ACTIFS : prise atomique puis chargement pour la catégorie.
        var products = await AcquireProductsAsync(lines, cancellationToken);

        // --- 3) Classification cohérente (P3-11) : le type de chaque ligne doit s'accorder avec la catégorie
        // RÉELLE de son produit sur un seul et même moment de consommation du stock. Vérifié ICI, une fois les
        // produits réellement acquis et AVANT toute écriture ou numérotation.
        RequireConsistentStockFlow(lines, products);

        // --- 4) Numéro de vente fiable (P2A-1E, R-03) : séquence déterministe attribuée DANS la transaction ; un
        // numéro attribué pour une vente annulée n'est pas consommé.
        var saleNumber = await _numberSequenceService.NextNumberAsync(DocumentSequenceNames.Sale, cancellationToken);

        // Une commande fournisseur naît dès qu'un article à consommation DIFFÉRÉE est présent — indépendamment de
        // IsCounterSale, comme avant P3-7. Le statut initial de la vente doit suivre CE fait, et non le type de
        // vente déclaré. Depuis P3-11, la question est posée au propriétaire unique de la classification, et la
        // garde ci-dessus garantit que la catégorie du produit donnerait la même réponse.
        var hasLenses = lines.Any(l => SaleLineStockFlowPolicy.IsFabricationItemType(l.ItemType));

        // --- 5) Vente : TOUS les montants proviennent de SalePricingPolicy, aucun de la commande.
        var sale = new Sale
        {
            CustomerId = command.CustomerId,
            SaleDate = now,
            SaleNumber = saleNumber,
            TotalAmount = pricing.TotalAmount,
            DiscountAmount = pricing.DiscountAmount,
            FinalAmount = pricing.FinalAmount,
            DepositAmount = pricing.DepositAmount,
            RemainingAmount = pricing.RemainingAmount,
            // Statut de paiement EXPLICITE : le défaut EF (Paid) marquait « payée » toute vente à crédit.
            PaymentStatus = pricing.PaymentStatus,
            PaymentMethod = command.PaymentMethod,
            Notes = command.Notes
        };

        // État initial cohérent (P3-7) : une vente qui attend des verres ne peut pas naître « livrée », même
        // déclarée « comptoir ». Aucune synchronisation ultérieure n'est introduite : ce statut ne bougera plus.
        sale.Status = hasLenses ? SaleStatus.AwaitingLenses : SaleStatus.Delivered;
        // Échéance de livraison : sémantique d'origine conservée telle quelle (pilotée par IsCounterSale). Son
        // désalignement possible avec le statut reste une dette documentée, hors périmètre P3-7.
        sale.EstimatedDelivery = command.IsCounterSale ? now : now.AddDays(14);

        // --- 6) Lignes : TotalPrice calculé (jamais fourni), données optiques validées et canoniques.
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            sale.SaleItems.Add(new SaleItem
            {
                ProductId = line.ProductId,
                ItemType = line.ItemType,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                TotalPrice = pricing.Lines[i].LineTotal,
                UsageType = line.UsageType,
                Sphere = line.Optics.Sphere,
                Cylinder = line.Optics.Cylinder,
                Axis = line.Optics.Axis,
                Addition = line.Optics.Addition,
                PrismValue = line.Optics.PrismValue,
                PrismBase = line.Optics.PrismBase,
                VisualAcuity = line.VisualAcuity
            });
        }

        Order? createdOrder = null;
        string? orderNumber = null;

        // Sauvegarder la vente (obtenir SaleId, nécessaire à l'Order ci-dessous).
        await _saleRepository.CreateAsync(sale, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // --- 7) Commande fournisseur si la vente contient des verres.
        if (hasLenses)
        {
            // Numéro de commande fournisseur fiable (P2A-1E, R-03), même transaction que la vente.
            orderNumber = await _numberSequenceService.NextNumberAsync(DocumentSequenceNames.Order, cancellationToken);

            var order = new Order
            {
                SaleId = sale.SaleId,
                OrderNumber = orderNumber,
                OrderDate = now,
                EstimatedDelivery = now.AddDays(14),
                Status = OrderStatus.New,
                Notes = $"Commande verres pour vente {sale.SaleNumber}"
            };

            // Ajouter uniquement les articles à consommation différée à la commande fournisseur (mêmes données
            // optiques canoniques). Même propriétaire de classification que `hasLenses` ci-dessus : la commande ne
            // peut donc jamais être créée sans ligne, ni recevoir une ligne consommée à la vente.
            foreach (var line in lines.Where(l => SaleLineStockFlowPolicy.IsFabricationItemType(l.ItemType)))
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = line.ProductId,
                    ItemType = line.ItemType,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    UsageType = line.UsageType,
                    Sphere = line.Optics.Sphere,
                    Cylinder = line.Optics.Cylinder,
                    Axis = line.Optics.Axis,
                    Addition = line.Optics.Addition,
                    PrismValue = line.Optics.PrismValue,
                    PrismBase = line.Optics.PrismBase,
                    VisualAcuity = line.VisualAcuity
                });
            }

            await _orderRepository.CreateAsync(order, cancellationToken);
            createdOrder = order;
        }

        // --- 8) Décrément du stock des produits NON-VERRE à l'enregistrement de la vente, que la vente soit
        // comptoir OU fabrication (P3-5). Les VERRES / LENTILLES sont commandés aux fournisseurs (mis dans l'Order
        // ci-dessus) et décrémentés plus tard, au passage en fabrication (AdvanceOrderStatusUseCase). Un non-verre
        // n'entrant jamais dans l'Order, il n'est décrémenté qu'une fois ici.
        foreach (var line in lines)
        {
            // Le produit est garanti chargé : son absence a déjà été refusée par la prise atomique (§2).
            var product = products[line.ProductId];

            // Exclure les articles à consommation différée de la décrémentation à la vente (décrémentés à la
            // fabrication). Même propriétaire de classification que le versement dans la commande ci-dessus : la
            // garde P3-11 ayant prouvé que les deux clés s'accordent, « sauté ici » ⇔ « présent dans la commande ».
            if (SaleLineStockFlowPolicy.IsDeferredStockCategory(product.Category))
                continue;

            // Décrément atomique conditionnel (P2A-1D, R-09) : ne rend jamais le stock négatif et élimine la mise
            // à jour perdue. En cas de stock insuffisant (ou modifié entre-temps), une InsufficientStockException
            // est levée → le runner annule TOUTE la vente, y compris les décréments des lignes précédentes.
            await _stockMutationService.DecrementStockAsync(line.ProductId, line.Quantity, cancellationToken);

            // Mouvement de stock (sortie) : Quantity négative (convention de signe P3-5).
            var stockMovement = new StockMovement
            {
                ProductId = line.ProductId,
                MovementType = StockMovementType.Out,
                Quantity = -line.Quantity, // Négatif pour sortie
                // Le motif reste un lien TEXTUEL (aucune FK Sale, dette D5) : il doit rester lisible même sans
                // client, plutôt que d'afficher un « Client # » sans identifiant.
                Reason = command.CustomerId.HasValue
                    ? $"Vente {sale.SaleNumber} - Client #{command.CustomerId.Value}"
                    : $"Vente {sale.SaleNumber} - Vente sans client",
                CreatedAt = now
            };
            await _stockMovementRepository.CreateAsync(stockMovement, cancellationToken);
        }

        // Sauvegarder toutes les modifications (commande + mouvements de stock).
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

    /// <summary>
    /// Exige que chaque ligne soit classée <b>comme son produit</b> (P3-11) : le type de ligne et la catégorie
    /// réelle du produit doivent désigner le <b>même</b> moment de consommation du stock.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Seule la catégorie chargée depuis la base fait foi.</b> Aucune catégorie fournie par la commande, par
    /// l'UI, par le nom ou par la référence du produit n'est consultée : les produits ont été acquis fraîchement et
    /// sans suivi juste avant, et c'est cette valeur — et elle seule — qui est opposée.
    /// </para>
    /// <para>
    /// <b>Refus avant toute écriture.</b> La garde s'exécute après l'acquisition des produits mais avant le numéro
    /// de vente, le numéro de commande, la vente, ses lignes, la commande, le décrément, le mouvement et toute
    /// sauvegarde : une ligne incohérente ne consomme donc <b>aucune</b> séquence et ne laisse aucune trace.
    /// </para>
    /// </remarks>
    private static void RequireConsistentStockFlow(
        IReadOnlyList<ValidatedSaleLine> lines,
        IReadOnlyDictionary<long, Product> products)
    {
        foreach (var line in lines)
        {
            // Le produit est garanti chargé : son absence a déjà été refusée par la prise atomique.
            var product = products[line.ProductId];

            if (!SaleLineStockFlowPolicy.IsCompatible(line.ItemType, product.Category))
                throw new BusinessRuleException(SaleLineProductCategoryMismatchMessage);
        }
    }

    /// <summary>
    /// Prend atomiquement le client <b>actif</b>, s'il est fourni. Sans client, aucune lecture n'est effectuée et
    /// la vente est acceptée telle quelle (<c>Sale.CustomerId = null</c>).
    /// </summary>
    private async Task AcquireCustomerAsync(long? customerId, CancellationToken cancellationToken)
    {
        if (!customerId.HasValue)
            return; // Vente sans client : cas nominal du comptoir anonyme.

        if (await _customerRepository.TryAcquireActiveAsync(customerId.Value, cancellationToken))
            return;

        // Aucune ligne prise. La lecture qui suit est PUREMENT DIAGNOSTIQUE : elle sert seulement à distinguer
        // « introuvable » d'« archivé » pour le message, et ne décide jamais de l'écriture (celle-ci a déjà été
        // refusée par la condition atomique). Lecture FRAÎCHE et NON SUIVIE (contourne le change tracker) : ne peut
        // jamais retomber sur une entité Customer déjà suivie et périmée dans le DbContext de la portée.
        var customer = await _customerRepository.GetByIdFreshAsync(customerId.Value, cancellationToken);

        throw new BusinessRuleException(customer is null ? CustomerNotFoundMessage : CustomerArchivedMessage);
    }

    /// <summary>
    /// Prend atomiquement chaque produit <b>actif</b> distinct de la commande, puis le charge (sa catégorie réelle
    /// décide du moment du décrément de stock). La déduplication garantit qu'un produit présent sur plusieurs
    /// lignes n'est pris et chargé <b>qu'une seule fois</b> — les lignes multiples restent parfaitement valides et
    /// chacune décrémente sa propre quantité.
    /// </summary>
    /// <remarks>
    /// <b>Ordre déterministe (P3-7, revue avant commit).</b> Les identifiants distincts sont pris
    /// <b>par ordre croissant de <see cref="Product.ProductId"/></b>, jamais dans l'ordre du panier ni celui d'un
    /// <see cref="HashSet{T}"/>/dictionnaire. Deux ventes contenant les mêmes produits dans des ordres de panier
    /// opposés acquièrent donc toujours leurs verrous dans la <b>même</b> séquence, ce qui réduit le risque
    /// d'interblocage sur un futur provider serveur (plusieurs connexions concurrentes).
    /// </remarks>
    private async Task<IReadOnlyDictionary<long, Product>> AcquireProductsAsync(
        IReadOnlyList<ValidatedSaleLine> lines,
        CancellationToken cancellationToken)
    {
        var products = new Dictionary<long, Product>();

        foreach (var productId in lines.Select(l => l.ProductId).Distinct().OrderBy(id => id))
        {
            if (!await _productRepository.TryAcquireActiveAsync(productId, cancellationToken))
            {
                // Lecture PUREMENT DIAGNOSTIQUE (introuvable vs désactivé) : la prise a déjà tranché. Aucun détail
                // technique EF/SQLite n'est exposé — uniquement un message métier stable. Lecture FRAÎCHE et NON
                // SUIVIE (contourne le change tracker) : ne peut jamais retomber sur une entité déjà suivie et
                // périmée dans le DbContext de la portée.
                var refused = await _productRepository.GetByIdFreshAsync(productId, cancellationToken);
                throw new BusinessRuleException(refused is null ? ProductNotFoundMessage : ProductInactiveMessage);
            }

            // La catégorie est lue par le BACKEND : l'appelant ne peut ni la fournir ni la contourner. Lecture
            // FRAÎCHE et NON SUIVIE : la prise atomique (ExecuteUpdateAsync) ne met à jour aucune entité suivie ;
            // sans cette lecture non-tracking, une entité Product déjà suivie (chargée plus tôt dans le même
            // DbContext, p. ex. depuis un écran catalogue) serait renvoyée telle quelle par FindAsync, avec une
            // catégorie potentiellement périmée décidant à tort du moment du décrément de stock.
            var product = await _productRepository.GetByIdFreshAsync(productId, cancellationToken)
                          ?? throw new BusinessRuleException(ProductNotFoundMessage);

            products.Add(productId, product);
        }

        return products;
    }

    /// <summary>
    /// Ligne de vente <b>déjà validée</b> : produit garanti présent, quantité et prix bornés, données optiques
    /// vérifiées et canoniques. Interdit par construction qu'une ligne non validée atteigne la persistance.
    /// </summary>
    private sealed record ValidatedSaleLine(
        long ProductId,
        OrderItemType ItemType,
        int Quantity,
        decimal UnitPrice,
        LensUsageType? UsageType,
        SaleLineOptics Optics,
        string? VisualAcuity);
}
