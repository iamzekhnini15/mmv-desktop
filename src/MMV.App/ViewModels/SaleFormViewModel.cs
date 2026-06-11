using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour le formulaire de création d'une vente complète.
/// Intègre la suggestion intelligente de produits basée sur l'ordonnance du client.
/// Supporte 2 modes : vente avec fabrication (défaut) ou vente comptoir immédiate.
/// </summary>
public class SaleFormViewModel : BaseViewModel
{
    private readonly ISaleRepository? _saleRepository;
    private readonly IOrderRepository? _orderRepository;
    private readonly IProductRepository? _productRepository;
    private readonly IPrescriptionRepository? _prescriptionRepository;
    private readonly IStockMovementRepository? _stockMovementRepository;
    private readonly IUnitOfWork? _unitOfWork;

    /// <summary>
    /// Frontière transactionnelle (P2A-1C, R-23) : <b>obligatoire</b>. Le flux multi-écriture
    /// <see cref="ExecuteSave"/> ne possède plus de repli non transactionnel ; l'absence de runner
    /// est une erreur de configuration (le constructeur la rejette).
    /// </summary>
    private readonly ITransactionRunner _transactionRunner;

    private long _customerId;
    private decimal _totalAmount;
    private decimal _discountAmount = 0;
    private decimal _finalAmount;
    private decimal _depositAmount = 0;
    private decimal _remainingAmount;
    private string _selectedPaymentMethod = "Espèces";
    private string _notes = string.Empty;
    private bool _isSaving;
    private bool _isLoadingProducts;
    private bool _isCounterSale = false;
    private ObservableCollection<OrderItem> _orderItems = new();

    // Prescription
    private Prescription? _activePrescription;
    private bool _hasPrescription;
    private string _prescriptionSummary = string.Empty;
    private string _suggestedGlassType = string.Empty;

    // Produits
    private ObservableCollection<Product> _allProducts = new();
    private ObservableCollection<Product> _filteredProducts = new();
    private ObservableCollection<Product> _suggestedGlasses = new();
    private ObservableCollection<Product> _compatibleFrames = new();
    private string _productSearchText = string.Empty;
    private string _selectedCategoryFilter = "Tous";
    private Product? _selectedProduct;
    private int _addQuantity = 1;

    #region Properties - Vente

    /// <summary>
    /// ID du client pour cette vente.
    /// </summary>
    public long CustomerId
    {
        get => _customerId;
        set => SetProperty(ref _customerId, value);
    }

    /// <summary>
    /// Montant total de la vente (avant remise).
    /// </summary>
    public decimal TotalAmount
    {
        get => _totalAmount;
        set => SetProperty(ref _totalAmount, value);
    }

    /// <summary>
    /// Montant de la réduction.
    /// </summary>
    public decimal DiscountAmount
    {
        get => _discountAmount;
        set
        {
            if (SetProperty(ref _discountAmount, value))
            {
                CalculateFinalAmount();
            }
        }
    }

    /// <summary>
    /// Montant final après remise.
    /// </summary>
    public decimal FinalAmount
    {
        get => _finalAmount;
        set => SetProperty(ref _finalAmount, value);
    }

    /// <summary>
    /// Montant de l'acompte versé (pour commandes de fabrication).
    /// </summary>
    public decimal DepositAmount
    {
        get => _depositAmount;
        set
        {
            if (SetProperty(ref _depositAmount, value))
            {
                CalculateRemainingAmount();
            }
        }
    }

    /// <summary>
    /// Montant restant à payer.
    /// </summary>
    public decimal RemainingAmount
    {
        get => _remainingAmount;
        set => SetProperty(ref _remainingAmount, value);
    }

    /// <summary>
    /// Méthode de paiement sélectionnée (string pour binding ComboBox).
    /// </summary>
    public string SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set => SetProperty(ref _selectedPaymentMethod, value);
    }

    /// <summary>
    /// Options de paiement disponibles.
    /// </summary>
    public ObservableCollection<string> PaymentMethodOptions { get; } = new()
    {
        "Espèces", "Carte Bancaire", "Chèque", "Virement"
    };

    /// <summary>
    /// Notes supplémentaires.
    /// </summary>
    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>
    /// Indique si la vente est en cours de sauvegarde.
    /// </summary>
    public bool IsSaving
    {
        get => _isSaving;
        set => SetProperty(ref _isSaving, value);
    }

    /// <summary>
    /// Indique si les produits sont en cours de chargement.
    /// </summary>
    public bool IsLoadingProducts
    {
        get => _isLoadingProducts;
        set => SetProperty(ref _isLoadingProducts, value);
    }

    /// <summary>
    /// Indique s'il s'agit d'une vente comptoir immédiate (true) ou d'une commande de fabrication (false).
    /// </summary>
    public bool IsCounterSale
    {
        get => _isCounterSale;
        set => SetProperty(ref _isCounterSale, value);
    }

    /// <summary>
    /// Éléments de la commande/vente.
    /// </summary>
    public ObservableCollection<OrderItem> OrderItems
    {
        get => _orderItems;
        set => SetProperty(ref _orderItems, value);
    }

    #endregion

    #region Properties - Prescription

    /// <summary>
    /// Ordonnance active du client (la plus récente).
    /// </summary>
    public Prescription? ActivePrescription
    {
        get => _activePrescription;
        set
        {
            if (SetProperty(ref _activePrescription, value))
            {
                HasPrescription = value != null;
                UpdatePrescriptionSummary();
            }
        }
    }

    /// <summary>
    /// Indique si le client a une ordonnance.
    /// </summary>
    public bool HasPrescription
    {
        get => _hasPrescription;
        set => SetProperty(ref _hasPrescription, value);
    }

    /// <summary>
    /// Résumé texte de l'ordonnance (ex: "Myopie OD -2.50 / OG -3.00").
    /// </summary>
    public string PrescriptionSummary
    {
        get => _prescriptionSummary;
        set => SetProperty(ref _prescriptionSummary, value);
    }

    /// <summary>
    /// Type de verre suggéré (SF, DF, MF) basé sur l'ordonnance.
    /// </summary>
    public string SuggestedGlassType
    {
        get => _suggestedGlassType;
        set => SetProperty(ref _suggestedGlassType, value);
    }

    #endregion

    #region Properties - Produits

    /// <summary>
    /// Tous les produits actifs.
    /// </summary>
    public ObservableCollection<Product> AllProducts
    {
        get => _allProducts;
        set => SetProperty(ref _allProducts, value);
    }

    /// <summary>
    /// Produits filtrés par recherche et catégorie.
    /// </summary>
    public ObservableCollection<Product> FilteredProducts
    {
        get => _filteredProducts;
        set => SetProperty(ref _filteredProducts, value);
    }

    /// <summary>
    /// Verres suggérés compatibles avec l'ordonnance.
    /// </summary>
    public ObservableCollection<Product> SuggestedGlasses
    {
        get => _suggestedGlasses;
        set => SetProperty(ref _suggestedGlasses, value);
    }

    /// <summary>
    /// Montures compatibles.
    /// </summary>
    public ObservableCollection<Product> CompatibleFrames
    {
        get => _compatibleFrames;
        set => SetProperty(ref _compatibleFrames, value);
    }

    /// <summary>
    /// Texte de recherche produit.
    /// </summary>
    public string ProductSearchText
    {
        get => _productSearchText;
        set
        {
            if (SetProperty(ref _productSearchText, value))
            {
                ApplyProductFilter();
            }
        }
    }

    /// <summary>
    /// Filtre de catégorie sélectionné.
    /// </summary>
    public string SelectedCategoryFilter
    {
        get => _selectedCategoryFilter;
        set
        {
            if (SetProperty(ref _selectedCategoryFilter, value))
            {
                ApplyProductFilter();
            }
        }
    }

    /// <summary>
    /// Produit sélectionné dans la liste pour ajout.
    /// </summary>
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set => SetProperty(ref _selectedProduct, value);
    }

    /// <summary>
    /// Quantité à ajouter pour le produit sélectionné.
    /// </summary>
    public int AddQuantity
    {
        get => _addQuantity;
        set => SetProperty(ref _addQuantity, Math.Max(1, value));
    }

    /// <summary>
    /// Catégories disponibles pour le filtre.
    /// </summary>
    public ObservableCollection<string> CategoryFilters { get; } = new()
    {
        "Tous", "Verres suggérés", "Montures", "Verres", "Lentilles", "Solaires", "Accessoires"
    };

    #endregion

    #region Commands

    /// <summary>
    /// Commande pour sauvegarder la vente.
    /// </summary>
    public ICommand SaveCommand { get; }

    /// <summary>
    /// Commande pour annuler.
    /// </summary>
    public ICommand CancelCommand { get; }

    /// <summary>
    /// Commande pour ajouter le produit sélectionné au panier.
    /// </summary>
    public ICommand AddToCartCommand { get; }

    /// <summary>
    /// Commande pour retirer un article du panier.
    /// </summary>
    public ICommand RemoveFromCartCommand { get; }

    #endregion

    #region Events

    /// <summary>
    /// Événement déclenché quand la vente est sauvegardée.
    /// </summary>
    public event EventHandler<Sale>? OrderSaved;

    /// <summary>
    /// Événement déclenché quand on annule.
    /// </summary>
    public event EventHandler? Cancelled;

    #endregion

    public SaleFormViewModel(
        ISaleRepository? saleRepository,
        IOrderRepository? orderRepository,
        IProductRepository? productRepository,
        IPrescriptionRepository? prescriptionRepository,
        IStockMovementRepository? stockMovementRepository,
        IUnitOfWork? unitOfWork,
        ITransactionRunner transactionRunner)
    {
        _saleRepository = saleRepository;
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _prescriptionRepository = prescriptionRepository;
        _stockMovementRepository = stockMovementRepository;
        _unitOfWork = unitOfWork;
        // Frontière transactionnelle obligatoire : pas d'exécution multi-écriture sans transaction.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));

        SaveCommand = new RelayCommand(ExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel);
        AddToCartCommand = new RelayCommand<Product>(ExecuteAddToCart);
        RemoveFromCartCommand = new RelayCommand<OrderItem>(ExecuteRemoveFromCart);

        Title = "Nouvelle Vente";
    }

    /// <summary>
    /// Initialise le ViewModel pour un client donné.
    /// Charge l'ordonnance la plus récente et les produits compatibles.
    /// </summary>
    public async Task InitializeForCustomerAsync(long customerId)
    {
        CustomerId = customerId;
        IsLoadingProducts = true;

        try
        {
            // Charger l'ordonnance la plus récente du client
            if (_prescriptionRepository != null)
            {
                ActivePrescription = await _prescriptionRepository.GetLatestByCustomerIdAsync(customerId);
            }

            // Charger tous les produits actifs
            if (_productRepository != null)
            {
                var products = await _productRepository.GetAllAsync();
                AllProducts = new ObservableCollection<Product>(products.Where(p => p.IsActive));

                // Filtrer les verres compatibles avec l'ordonnance
                if (ActivePrescription != null)
                {
                    FilterGlassesByPrescription();
                }

                // Charger les montures
                CompatibleFrames = new ObservableCollection<Product>(
                    AllProducts.Where(p => p.Category == ProductCategoryEnum.MONTURE && p.StockQuantity > 0)
                    .OrderBy(p => p.Name));

                // Appliquer le filtre initial (verres suggérés si ordonnance, sinon tous)
                SelectedCategoryFilter = HasPrescription ? "Verres suggérés" : "Tous";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement : {ex.Message}";
        }
        finally
        {
            IsLoadingProducts = false;
        }
    }

    /// <summary>
    /// Filtre les verres compatibles selon l'ordonnance du client.
    /// Prend en compte : type de verre (SF/DF/MF) et limites de puissance.
    /// </summary>
    private void FilterGlassesByPrescription()
    {
        if (ActivePrescription == null) return;

        var glasses = AllProducts.Where(p => p.Category == ProductCategoryEnum.VERRE && p.GlassDetail != null).ToList();
        var compatible = new List<Product>();

        // Déterminer le type de verre nécessaire
        var suggestedType = DetermineSuggestedGlassType(ActivePrescription);

        // Déterminer la puissance max requise (valeur absolue la plus élevée entre OD et OG)
        double maxPower = GetMaxRequiredPower(ActivePrescription);

        foreach (var glass in glasses)
        {
            var detail = glass.GlassDetail!;

            // 1. Filtrer par type de verre
            if (detail.GlassType.HasValue && suggestedType != null)
            {
                // Ne proposer que les verres du bon type
                if (detail.GlassType.Value != suggestedType.Value)
                    continue;
            }

            // 2. Filtrer par limites de puissance
            if (detail.PowerLimitMin.HasValue && detail.PowerLimitMax.HasValue)
            {
                double limitMin = (double)detail.PowerLimitMin.Value;
                double limitMax = (double)detail.PowerLimitMax.Value;

                // Vérifier que la correction OD est dans les limites
                if (ActivePrescription.OdSphere.HasValue)
                {
                    double odSphere = ActivePrescription.OdSphere.Value;
                    if (odSphere < limitMin || odSphere > limitMax)
                        continue;
                }

                // Vérifier que la correction OG est dans les limites
                if (ActivePrescription.OgSphere.HasValue)
                {
                    double ogSphere = ActivePrescription.OgSphere.Value;
                    if (ogSphere < limitMin || ogSphere > limitMax)
                        continue;
                }

                // Si cylindre, vérifier aussi
                if (ActivePrescription.OdCylinder.HasValue)
                {
                    double odCyl = ActivePrescription.OdCylinder.Value;
                    // La puissance totale = sphère + cylindre
                    double odTotal = (ActivePrescription.OdSphere ?? 0) + odCyl;
                    if (odTotal < limitMin || odTotal > limitMax)
                        continue;
                }

                if (ActivePrescription.OgCylinder.HasValue)
                {
                    double ogCyl = ActivePrescription.OgCylinder.Value;
                    double ogTotal = (ActivePrescription.OgSphere ?? 0) + ogCyl;
                    if (ogTotal < limitMin || ogTotal > limitMax)
                        continue;
                }
            }

            // 3. Vérifier le stock
            if (glass.StockQuantity > 0)
            {
                compatible.Add(glass);
            }
        }

        SuggestedGlasses = new ObservableCollection<Product>(compatible.OrderBy(p => p.SalePrice));
    }

    /// <summary>
    /// Détermine le type de verre suggéré selon l'ordonnance.
    /// - Pas d'addition → SF (Simple Foyer)
    /// - Addition présente → MF (Progressif) ou DF (Double Foyer)
    /// </summary>
    private GlassType? DetermineSuggestedGlassType(Prescription prescription)
    {
        bool hasAddition = (prescription.OdAddition.HasValue && prescription.OdAddition.Value > 0)
                        || (prescription.OgAddition.HasValue && prescription.OgAddition.Value > 0);

        if (!hasAddition)
        {
            // Pas d'addition = correction simple (myopie, hypermétropie, astigmatisme)
            SuggestedGlassType = "Simple Foyer (SF) — Correction simple";
            return GlassType.SF;
        }
        else
        {
            // Addition présente = presbytie → progressif recommandé
            SuggestedGlassType = "Progressif (MF) — Correction vision de près et de loin";
            return GlassType.MF;
        }
    }

    /// <summary>
    /// Calcule la puissance maximale requise par l'ordonnance.
    /// </summary>
    private double GetMaxRequiredPower(Prescription prescription)
    {
        double max = 0;

        if (prescription.OdSphere.HasValue)
            max = Math.Max(max, Math.Abs(prescription.OdSphere.Value));
        if (prescription.OgSphere.HasValue)
            max = Math.Max(max, Math.Abs(prescription.OgSphere.Value));

        return max;
    }

    /// <summary>
    /// Met à jour le résumé textuel de l'ordonnance.
    /// </summary>
    private void UpdatePrescriptionSummary()
    {
        if (ActivePrescription == null)
        {
            PrescriptionSummary = "Aucune ordonnance";
            SuggestedGlassType = "";
            return;
        }

        var parts = new List<string>();

        // OD
        if (ActivePrescription.OdSphere.HasValue)
        {
            string odStr = $"OD : Sph {FormatDiopter(ActivePrescription.OdSphere.Value)}";
            if (ActivePrescription.OdCylinder.HasValue)
                odStr += $" Cyl {FormatDiopter(ActivePrescription.OdCylinder.Value)}";
            if (ActivePrescription.OdAxis.HasValue)
                odStr += $" Axe {ActivePrescription.OdAxis.Value}°";
            if (ActivePrescription.OdAddition.HasValue && ActivePrescription.OdAddition.Value > 0)
                odStr += $" Add +{ActivePrescription.OdAddition.Value:F2}";
            parts.Add(odStr);
        }

        // OG
        if (ActivePrescription.OgSphere.HasValue)
        {
            string ogStr = $"OG : Sph {FormatDiopter(ActivePrescription.OgSphere.Value)}";
            if (ActivePrescription.OgCylinder.HasValue)
                ogStr += $" Cyl {FormatDiopter(ActivePrescription.OgCylinder.Value)}";
            if (ActivePrescription.OgAxis.HasValue)
                ogStr += $" Axe {ActivePrescription.OgAxis.Value}°";
            if (ActivePrescription.OgAddition.HasValue && ActivePrescription.OgAddition.Value > 0)
                ogStr += $" Add +{ActivePrescription.OgAddition.Value:F2}";
            parts.Add(ogStr);
        }

        PrescriptionSummary = parts.Count > 0 ? string.Join(" | ", parts) : "Ordonnance sans correction";

        // Déterminer les pathologies détectées
        DetermineSuggestedGlassType(ActivePrescription);
    }

    /// <summary>
    /// Formate une valeur dioptrique (+/-).
    /// </summary>
    private string FormatDiopter(double value)
    {
        return value >= 0 ? $"+{value:F2}" : $"{value:F2}";
    }

    /// <summary>
    /// Applique le filtre de recherche/catégorie sur les produits.
    /// </summary>
    private void ApplyProductFilter()
    {
        IEnumerable<Product> source;

        switch (SelectedCategoryFilter)
        {
            case "Verres suggérés":
                source = SuggestedGlasses;
                break;
            case "Montures":
                source = AllProducts.Where(p => p.Category == ProductCategoryEnum.MONTURE);
                break;
            case "Verres":
                source = AllProducts.Where(p => p.Category == ProductCategoryEnum.VERRE);
                break;
            case "Lentilles":
                source = AllProducts.Where(p => p.Category == ProductCategoryEnum.LENTILLE);
                break;
            case "Solaires":
                source = AllProducts.Where(p => p.Category == ProductCategoryEnum.SOLAIRE);
                break;
            case "Accessoires":
                source = AllProducts.Where(p => p.Category == ProductCategoryEnum.CLIPS || p.Category == ProductCategoryEnum.PLASTIC);
                break;
            default: // "Tous"
                source = AllProducts;
                break;
        }

        // Appliquer la recherche textuelle
        if (!string.IsNullOrWhiteSpace(ProductSearchText))
        {
            var search = ProductSearchText.ToLower();
            source = source.Where(p =>
                p.Name.ToLower().Contains(search) ||
                p.Reference.ToLower().Contains(search) ||
                (p.Description?.ToLower().Contains(search) ?? false));
        }

        // Filtrer par stock : exclure les produits sans stock SAUF les verres (commandés aux fournisseurs)
        source = source.Where(p => 
            p.StockQuantity > 0 || 
            p.Category == ProductCategoryEnum.VERRE || 
            p.Category == ProductCategoryEnum.LENTILLE);

        FilteredProducts = new ObservableCollection<Product>(source.OrderBy(p => p.Name));
    }

    /// <summary>
    /// Ajoute le produit sélectionné au panier.
    /// Pour les verres : crée automatiquement 2 articles (OD + OG) avec les paramètres de l'ordonnance.
    /// </summary>
    private void ExecuteAddToCart(Product? product)
    {
        if (product == null) return;

        // Si c'est un verre ou une lentille
        bool isLens = product.Category == ProductCategoryEnum.VERRE || product.Category == ProductCategoryEnum.LENTILLE;

        if (isLens)
        {
            // Vérifier qu'il y a une ordonnance
            if (ActivePrescription == null)
            {
                ErrorMessage = "⚠️ Impossible d'ajouter un verre sans ordonnance. Veuillez d'abord ajouter une ordonnance au client.";
                return;
            }

            // Créer 2 OrderItem : un pour l'œil droit (OD) et un pour l'œil gauche (OG)
            var orderItemOd = new OrderItem
            {
                ProductId = product.ProductId,
                Product = product,
                ItemType = OrderItemType.LensOd,
                Quantity = AddQuantity,
                UnitPrice = product.SalePrice,
                // Paramètres de correction depuis l'ordonnance (OD)
                Sphere = ActivePrescription.OdSphere,
                Cylinder = ActivePrescription.OdCylinder,
                Axis = ActivePrescription.OdAxis,
                Addition = ActivePrescription.OdAddition,
                PrismValue = ActivePrescription.OdPrismValue,
                PrismBase = ActivePrescription.OdPrismBase,
                VisualAcuity = ActivePrescription.OdVisualAcuity,
                UsageType = DetermineUsageType(ActivePrescription.OdAddition)
            };

            var orderItemOg = new OrderItem
            {
                ProductId = product.ProductId,
                Product = product,
                ItemType = OrderItemType.LensOg,
                Quantity = AddQuantity,
                UnitPrice = product.SalePrice,
                // Paramètres de correction depuis l'ordonnance (OG)
                Sphere = ActivePrescription.OgSphere,
                Cylinder = ActivePrescription.OgCylinder,
                Axis = ActivePrescription.OgAxis,
                Addition = ActivePrescription.OgAddition,
                PrismValue = ActivePrescription.OgPrismValue,
                PrismBase = ActivePrescription.OgPrismBase,
                VisualAcuity = ActivePrescription.OgVisualAcuity,
                UsageType = DetermineUsageType(ActivePrescription.OgAddition)
            };

            OrderItems.Add(orderItemOd);
            OrderItems.Add(orderItemOg);
        }
        else
        {
            // Pour les montures et accessoires : comportement normal
            var existing = OrderItems.FirstOrDefault(oi => oi.ProductId == product.ProductId && oi.ItemType == DetermineOrderItemType(product));
            if (existing != null)
            {
                // Incrémenter la quantité
                existing.Quantity += AddQuantity;
                // Forcer la mise à jour de la collection
                var index = OrderItems.IndexOf(existing);
                OrderItems[index] = existing;
            }
            else
            {
                var orderItem = new OrderItem
                {
                    ProductId = product.ProductId,
                    Product = product,
                    ItemType = DetermineOrderItemType(product),
                    Quantity = AddQuantity,
                    UnitPrice = product.SalePrice
                };
                OrderItems.Add(orderItem);
            }
        }

        AddQuantity = 1;
        CalculateFinalAmount();
        ErrorMessage = null; // Clear error if successful
    }

    /// <summary>
    /// Retire un article du panier.
    /// </summary>
    private void ExecuteRemoveFromCart(OrderItem? item)
    {
        if (item != null)
        {
            OrderItems.Remove(item);
            CalculateFinalAmount();
        }
    }

    /// <summary>
    /// Ajoute un article à la commande (API publique).
    /// </summary>
    public void AddOrderItem(OrderItem item)
    {
        OrderItems.Add(item);
        CalculateFinalAmount();
    }

    /// <summary>
    /// Supprime un article de la commande (API publique).
    /// </summary>
    public void RemoveOrderItem(OrderItem item)
    {
        OrderItems.Remove(item);
        CalculateFinalAmount();
    }

    /// <summary>
    /// Calcule le montant final de la commande/vente.
    /// </summary>
    private void CalculateFinalAmount()
    {
        decimal subtotal = 0;
        foreach (var item in OrderItems)
        {
            subtotal += item.UnitPrice * item.Quantity;
        }
        TotalAmount = subtotal;
        FinalAmount = subtotal - DiscountAmount;
        CalculateRemainingAmount();
    }

    /// <summary>
    /// Calcule le montant restant à payer.
    /// </summary>
    private void CalculateRemainingAmount()
    {
        RemainingAmount = FinalAmount - DepositAmount;
    }

    private async void ExecuteSave()
    {
        // Garde anti double-soumission (P2A-1C) : une sauvegarde déjà en cours bloque toute ré-entrée.
        // ExecuteSave est invoqué sur le thread UI ; IsSaving est positionné avant le premier await,
        // si bien qu'un second clic pendant la sauvegarde est rejeté immédiatement.
        if (IsSaving)
            return;

        if (_orderRepository == null || _unitOfWork == null || _productRepository == null || _stockMovementRepository == null)
        {
            ErrorMessage = "Repository non initialisé";
            return;
        }

        if (OrderItems.Count == 0)
        {
            ErrorMessage = "Ajoutez au moins un article au panier";
            return;
        }

        IsSaving = true;
        ErrorMessage = null;

        try
        {
            CalculateFinalAmount();

            // Frontière transactionnelle OBLIGATOIRE (P2A-1C, R-23) : la création de la vente,
            // l'éventuelle commande fournisseur et les mouvements de stock sont persistés de façon
            // atomique. En cas d'erreur au milieu de l'opération, le runner annule TOUT (aucune
            // écriture partielle). Il n'existe plus de repli non transactionnel : `_transactionRunner`
            // est garanti non nul par le constructeur.
            Sale savedSale = await _transactionRunner.RunAsync(PersistSaleAsync);

            // Notifier que la vente a été sauvegardée
            OrderSaved?.Invoke(this, savedSale);

            ClearForm();
        }
        catch (PersistenceException pex)
        {
            // Erreur de persistance contrôlée : message utilisateur déjà assaini (pas de fuite
            // technique), transaction annulée → aucune donnée partiellement enregistrée.
            ErrorMessage = pex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la sauvegarde : {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// Construit la vente puis la persiste (vente → éventuelle commande fournisseur → mouvements de
    /// stock). Conçue pour s'exécuter dans la frontière transactionnelle (<see cref="ITransactionRunner"/>) :
    /// toute exception levée ici provoque l'annulation complète de l'écriture.
    /// </summary>
    private async Task<Sale> PersistSaleAsync(CancellationToken cancellationToken)
    {
        // Créer la vente complète
        var sale = new Sale
        {
            CustomerId = CustomerId,
            SaleDate = DateTime.Now,
            SaleNumber = $"VTE-{DateTime.Now:yyyy}-{new Random().Next(10000):D4}",
            TotalAmount = TotalAmount,
            DiscountAmount = DiscountAmount,
            FinalAmount = FinalAmount,
            DepositAmount = DepositAmount,
            RemainingAmount = RemainingAmount,
            PaymentMethod = ConvertPaymentMethodFromString(SelectedPaymentMethod),
            Notes = Notes
        };

        // Configuration selon le type de vente
        if (IsCounterSale)
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
        foreach (var item in OrderItems)
        {
            sale.SaleItems.Add(new SaleItem
            {
                ProductId = item.ProductId,
                ItemType = item.ItemType == OrderItemType.Frame ? OrderItemType.Frame :
                          item.ItemType == OrderItemType.LensOd ? OrderItemType.LensOd :
                          item.ItemType == OrderItemType.LensOg ? OrderItemType.LensOg :
                          OrderItemType.Accessory,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                // Paramètres de correction (pour verres)
                UsageType = item.UsageType,
                Sphere = item.Sphere,
                Cylinder = item.Cylinder,
                Axis = item.Axis,
                Addition = item.Addition,
                PrismValue = item.PrismValue,
                PrismBase = item.PrismBase,
                VisualAcuity = item.VisualAcuity
            });
        }

        // Sauvegarder la vente
        if (_saleRepository != null)
        {
            await _saleRepository.CreateAsync(sale, cancellationToken);
            await _unitOfWork!.SaveChangesAsync(cancellationToken); // Sauvegarder pour obtenir SaleId

            // Si la vente contient des verres, créer automatiquement une commande fournisseur
            var hasLenses = OrderItems.Any(i => i.ItemType == OrderItemType.LensOd || i.ItemType == OrderItemType.LensOg);
            if (hasLenses && _orderRepository != null)
            {
                var order = new Order
                {
                    SaleId = sale.SaleId,
                    OrderNumber = $"CMD-{DateTime.Now:yyyy}-{new Random().Next(10000):D4}",
                    OrderDate = DateTime.Now,
                    EstimatedDelivery = DateTime.Now.AddDays(14),
                    Status = OrderStatus.New,
                    Notes = $"Commande verres pour vente {sale.SaleNumber}"
                };

                // Ajouter uniquement les verres à la commande fournisseur
                foreach (var item in OrderItems.Where(i => i.ItemType == OrderItemType.LensOd || i.ItemType == OrderItemType.LensOg))
                {
                    order.OrderItems.Add(new OrderItem
                    {
                        ProductId = item.ProductId,
                        ItemType = item.ItemType,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        UsageType = item.UsageType,
                        Sphere = item.Sphere,
                        Cylinder = item.Cylinder,
                        Axis = item.Axis,
                        Addition = item.Addition,
                        PrismValue = item.PrismValue,
                        PrismBase = item.PrismBase,
                        VisualAcuity = item.VisualAcuity
                    });
                }

                await _orderRepository.CreateAsync(order, cancellationToken);
            }
        }

        // Si vente comptoir : créer les mouvements de stock et décrémenter le stock
        // SAUF pour les verres (ils sont commandés aux fournisseurs)
        if (IsCounterSale)
        {
            foreach (var item in OrderItems)
            {
                // Vérifier que le ProductId existe
                if (!item.ProductId.HasValue)
                    continue;

                // Charger le produit avec détails
                var product = await _productRepository!.GetByIdAsync(item.ProductId.Value, cancellationToken);
                if (product != null)
                {
                    // Exclure les verres de la décrémentation du stock
                    bool isLens = product.Category == ProductCategoryEnum.VERRE || product.Category == ProductCategoryEnum.LENTILLE;
                    if (isLens)
                        continue; // Les verres sont commandés aux fournisseurs, pas en stock

                    // Décrémenter le stock (montures et accessoires uniquement)
                    product.StockQuantity -= item.Quantity;
                    await _productRepository.UpdateAsync(product, cancellationToken);

                    // Créer le mouvement de stock (sortie)
                    var stockMovement = new StockMovement
                    {
                        ProductId = item.ProductId.Value,
                        MovementType = StockMovementType.Out,
                        Quantity = -item.Quantity, // Négatif pour sortie
                        Reason = $"Vente comptoir {sale.SaleNumber} - Client #{CustomerId}",
                        CreatedAt = DateTime.Now
                    };
                    await _stockMovementRepository!.CreateAsync(stockMovement, cancellationToken);
                }
            }
        }

        // Sauvegarder toutes les modifications (commande + mouvements de stock)
        await _unitOfWork!.SaveChangesAsync(cancellationToken);

        return sale;
    }

    private void ExecuteCancel()
    {
        ClearForm();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void ClearForm()
    {
        TotalAmount = 0;
        DiscountAmount = 0;
        FinalAmount = 0;
        DepositAmount = 0;
        RemainingAmount = 0;
        SelectedPaymentMethod = "Espèces";
        Notes = string.Empty;
        OrderItems.Clear();
        AddQuantity = 1;
        SelectedProduct = null;
        ProductSearchText = string.Empty;
        IsCounterSale = false;
    }

    /// <summary>
    /// Détermine le type d'OrderItem en fonction de la catégorie du produit.
    /// </summary>
    private OrderItemType DetermineOrderItemType(Product product)
    {
        return product.Category switch
        {
            ProductCategoryEnum.MONTURE => OrderItemType.Frame,
            ProductCategoryEnum.VERRE => OrderItemType.LensOd, // Par défaut OD
            ProductCategoryEnum.LENTILLE => OrderItemType.LensOd,
            _ => OrderItemType.Accessory
        };
    }

    /// <summary>
    /// Détermine le type d'usage du verre en fonction de l'addition.
    /// </summary>
    private LensUsageType DetermineUsageType(double? addition)
    {
        if (addition.HasValue && addition.Value > 0)
        {
            return LensUsageType.Progressive; // Si addition présente → Progressif
        }
        return LensUsageType.Distance; // Par défaut → Distance
    }

    /// <summary>
    /// Convertit le string du ComboBox en enum PaymentMethod.
    /// </summary>
    private PaymentMethod ConvertPaymentMethodFromString(string method)
    {
        return method switch
        {
            "Espèces" => PaymentMethod.Cash,
            "Carte Bancaire" => PaymentMethod.Card,
            "Chèque" => PaymentMethod.Check,
            "Virement" => PaymentMethod.Transfer,
            _ => PaymentMethod.Cash
        };
    }
}
