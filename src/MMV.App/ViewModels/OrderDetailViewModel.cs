using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'une commande avec workflow de statut
/// et checklist contrôle qualité.
/// </summary>
public class OrderDetailViewModel : BaseViewModel
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly INotificationRepository? _notificationRepository;

    private Order? _order;
    private ObservableCollection<OrderItem> _items = new();

    // Workflow
    private bool _canAdvanceStatus;
    private bool _isOverdue;
    private int _daysRemaining;

    // Contrôle qualité
    private bool _checkFrameAlignment;
    private bool _checkLensOd;
    private bool _checkLensOg;
    private bool _checkCleanliness;
    private bool _checkOverall;

    #region Properties

    public Order? Order
    {
        get => _order;
        set
        {
            if (SetProperty(ref _order, value))
            {
                OnPropertyChanged(nameof(OrderNumber));
                OnPropertyChanged(nameof(CustomerName));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(NextStatusDisplay));
                OnPropertyChanged(nameof(TotalAmount));
                OnPropertyChanged(nameof(OrderDate));
                OnPropertyChanged(nameof(EstimatedDelivery));
                OnPropertyChanged(nameof(Notes));
                OnPropertyChanged(nameof(CurrentStatus));
                OnPropertyChanged(nameof(IsQualityCheckPhase));
                OnPropertyChanged(nameof(DepositAmount));
                OnPropertyChanged(nameof(RemainingAmount));
                OnPropertyChanged(nameof(PaymentMethod));
                OnPropertyChanged(nameof(HasRemainingBalance));
                UpdateWorkflowState();
            }
        }
    }

    public ObservableCollection<OrderItem> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    public string OrderNumber => Order?.OrderNumber ?? string.Empty;
    public string CustomerName => Order?.Sale?.Customer != null
        ? $"{Order.Sale.Customer.FirstName} {Order.Sale.Customer.LastName}"
        : "Client inconnu";
    public OrderStatus CurrentStatus => Order?.Status ?? OrderStatus.New;
    public string StatusDisplay => OrdersListViewModel.StatusEnumToDisplay(CurrentStatus);

    public string? NextStatusDisplay
    {
        get
        {
            var next = GetNextStatus();
            return next.HasValue ? OrdersListViewModel.StatusEnumToDisplay(next.Value) : null;
        }
    }

    public decimal TotalAmount => Order?.Sale?.FinalAmount ?? 0m;
    public DateTime OrderDate => Order?.OrderDate ?? DateTime.MinValue;
    public DateTime? EstimatedDelivery => Order?.EstimatedDelivery;
    public string Notes => Order?.Notes ?? string.Empty;

    /// <summary>
    /// Indique si on est dans la phase contrôle qualité.
    /// </summary>
    public bool IsQualityCheckPhase => CurrentStatus == OrderStatus.QualityCheck;

    /// <summary>
    /// Montant de l'acompte versé.
    /// </summary>
    public decimal? DepositAmount => Order?.Sale?.DepositAmount;

    /// <summary>
    /// Montant restant à payer.
    /// </summary>
    public decimal? RemainingAmount => Order?.Sale?.RemainingAmount;

    /// <summary>
    /// Moyen de paiement.
    /// </summary>
    public string PaymentMethod => Order?.Sale?.PaymentMethod.ToString() ?? "-";

    /// <summary>
    /// Indique s'il y a un solde restant à encaisser.
    /// </summary>
    public bool HasRemainingBalance => RemainingAmount.HasValue && RemainingAmount.Value > 0;

    public bool CanAdvanceStatus
    {
        get => _canAdvanceStatus;
        set => SetProperty(ref _canAdvanceStatus, value);
    }

    public bool IsOverdue
    {
        get => _isOverdue;
        set => SetProperty(ref _isOverdue, value);
    }

    public int DaysRemaining
    {
        get => _daysRemaining;
        set => SetProperty(ref _daysRemaining, value);
    }

    // Contrôle qualité
    public bool CheckFrameAlignment
    {
        get => _checkFrameAlignment;
        set
        {
            if (SetProperty(ref _checkFrameAlignment, value))
            {
                OnPropertyChanged(nameof(AllChecksComplete));
                UpdateWorkflowState();
            }
        }
    }

    public bool CheckLensOd
    {
        get => _checkLensOd;
        set
        {
            if (SetProperty(ref _checkLensOd, value))
            {
                OnPropertyChanged(nameof(AllChecksComplete));
                UpdateWorkflowState();
            }
        }
    }

    public bool CheckLensOg
    {
        get => _checkLensOg;
        set
        {
            if (SetProperty(ref _checkLensOg, value))
            {
                OnPropertyChanged(nameof(AllChecksComplete));
                UpdateWorkflowState();
            }
        }
    }

    public bool CheckCleanliness
    {
        get => _checkCleanliness;
        set
        {
            if (SetProperty(ref _checkCleanliness, value))
            {
                OnPropertyChanged(nameof(AllChecksComplete));
                UpdateWorkflowState();
            }
        }
    }

    public bool CheckOverall
    {
        get => _checkOverall;
        set
        {
            if (SetProperty(ref _checkOverall, value))
            {
                OnPropertyChanged(nameof(AllChecksComplete));
                UpdateWorkflowState();
            }
        }
    }

    /// <summary>
    /// Indique si tous les contrôles qualité sont validés.
    /// </summary>
    public bool AllChecksComplete =>
        CheckFrameAlignment && CheckLensOd && CheckLensOg && CheckCleanliness && CheckOverall;

    #endregion

    #region Commands

    public ICommand AdvanceStatusCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PrintFabSheetCommand { get; }
    public ICommand EncashBalanceCommand { get; }

    #endregion

    #region Events

    public event EventHandler? BackRequested;
    public event EventHandler<Order>? EditRequested;
    public event EventHandler<Order>? DeleteRequested;
    public event EventHandler<Order>? PrintFabSheetRequested;
    public event EventHandler<Order>? OrderUpdated;

    #endregion

    public OrderDetailViewModel(
        IOrderRepository orderRepository,
        IUnitOfWork unitOfWork,
        IStockMovementRepository stockMovementRepository,
        INotificationRepository? notificationRepository = null)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
        _stockMovementRepository = stockMovementRepository;
        _notificationRepository = notificationRepository;

        AdvanceStatusCommand = new RelayCommand(async () => await AdvanceStatusAsync(), () => CanAdvanceStatus);
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        EditCommand = new RelayCommand(() =>
        {
            if (Order != null) EditRequested?.Invoke(this, Order);
        });
        DeleteCommand = new RelayCommand(() =>
        {
            if (Order != null) DeleteRequested?.Invoke(this, Order);
        });
        PrintFabSheetCommand = new RelayCommand(() =>
        {
            if (Order != null) PrintFabSheetRequested?.Invoke(this, Order);
        });
        EncashBalanceCommand = new RelayCommand(async () => await ExecuteEncashBalanceAsync(), () => HasRemainingBalance);

        Title = "Détail Commande";
    }

    /// <summary>
    /// Initialise le détail avec une commande chargée.
    /// </summary>
    public void Initialize(Order order)
    {
        Order = order;
        Items = new ObservableCollection<OrderItem>(order.OrderItems ?? Array.Empty<OrderItem>());
        UpdateWorkflowState();
    }

    private void UpdateWorkflowState()
    {
        var next = GetNextStatus();
        bool canAdvance = next.HasValue;

        // En phase contrôle qualité, on ne peut avancer que si tous les checks sont OK
        if (CurrentStatus == OrderStatus.QualityCheck && !AllChecksComplete)
            canAdvance = false;

        // Commande déjà livrée
        if (CurrentStatus == OrderStatus.Delivered)
            canAdvance = false;

        CanAdvanceStatus = canAdvance;
        (AdvanceStatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EncashBalanceCommand as RelayCommand)?.RaiseCanExecuteChanged();

        // Calcul du retard
        if (Order?.EstimatedDelivery.HasValue == true && CurrentStatus != OrderStatus.Delivered)
        {
            var remaining = (Order.EstimatedDelivery!.Value - DateTime.Now).Days;
            DaysRemaining = remaining;
            IsOverdue = remaining < 0;
        }
        else
        {
            DaysRemaining = 0;
            IsOverdue = false;
        }
    }

    private OrderStatus? GetNextStatus()
    {
        return CurrentStatus switch
        {
            OrderStatus.New => OrderStatus.ToFabricate,
            OrderStatus.ToFabricate => OrderStatus.InProgress,
            OrderStatus.InProgress => OrderStatus.QualityCheck,
            OrderStatus.QualityCheck => OrderStatus.Ready,
            OrderStatus.Ready => OrderStatus.Delivered,
            _ => null
        };
    }

    /// <summary>
    /// Fait avancer le statut de la commande vers l'étape suivante.
    /// Gère la mise à jour du stock lors du passage en fabrication.
    /// Crée une notification de changement de statut.
    /// </summary>
    private async Task AdvanceStatusAsync()
    {
        var nextStatus = GetNextStatus();
        if (Order == null || !nextStatus.HasValue) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var previousStatus = Order.Status;

            // Recharger la commande fraîche pour éviter les conflits EF
            var fresh = await _orderRepository.GetWithItemsAsync(Order.OrderId);
            if (fresh == null)
            {
                ErrorMessage = "Commande introuvable.";
                return;
            }

            fresh.Status = nextStatus.Value;
            await _orderRepository.UpdateAsync(fresh);

            // Sortie de stock lors du passage en fabrication
            if (previousStatus == OrderStatus.ToFabricate && nextStatus.Value == OrderStatus.InProgress)
            {
                await CreateStockMovementsForFabrication(fresh);
            }

            // Créer une notification
            if (_notificationRepository != null)
            {
                var notification = new Notification
                {
                    Type = "OrderStatusChanged",
                    Title = $"Commande {fresh.OrderNumber} : {OrdersListViewModel.StatusEnumToDisplay(nextStatus.Value)}",
                    Message = $"La commande {fresh.OrderNumber} ({CustomerName}) est passée de " +
                              $"'{OrdersListViewModel.StatusEnumToDisplay(previousStatus)}' à " +
                              $"'{OrdersListViewModel.StatusEnumToDisplay(nextStatus.Value)}'.",
                    EntityId = fresh.OrderId,
                    EntityType = "Order",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };
                await _notificationRepository.CreateAsync(notification);
            }

            await _unitOfWork.SaveChangesAsync();

            // Mettre à jour l'état local
            Order = fresh;
            Items = new ObservableCollection<OrderItem>(fresh.OrderItems);

            // Signaler que la commande a été mise à jour pour rafraîchir la liste/Kanban
            OrderUpdated?.Invoke(this, fresh);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du changement de statut : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrderDetailViewModel] Status advance error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Crée des mouvements de stock de type OUT pour chaque article de la commande.
    /// </summary>
    private async Task CreateStockMovementsForFabrication(Order order)
    {
        foreach (var item in order.OrderItems.Where(i => i.ProductId.HasValue))
        {
            var movement = new StockMovement
            {
                ProductId = item.ProductId!.Value,
                MovementType = StockMovementType.Out,
                Quantity = item.Quantity,
                Reason = $"Fabrication commande {order.OrderNumber}",
                CreatedAt = DateTime.UtcNow,
            };

            await _stockMovementRepository.CreateAsync(movement);

            // Mettre à jour le stock du produit
            if (item.Product != null)
            {
                item.Product.StockQuantity -= item.Quantity;
                // Note: le SaveChanges est fait au niveau appelant
            }
        }
    }

    /// <summary>
    /// Encaisse le montant restant d'une commande.
    /// </summary>
    private async Task ExecuteEncashBalanceAsync()
    {
        if (Order == null || !HasRemainingBalance || Order.Sale == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            // Recharger la commande fraîche avec sa vente
            var fresh = await _orderRepository.GetWithItemsAsync(Order.OrderId);
            if (fresh == null || fresh.Sale == null)
            {
                ErrorMessage = "Commande introuvable.";
                return;
            }

            // Sauvegarder le montant restant avant modification pour la notification
            var amountToEncash = fresh.Sale.RemainingAmount ?? 0;

            // Mettre à jour le paiement sur la vente : acompte devient le montant total, restant devient 0
            fresh.Sale.DepositAmount = fresh.Sale.FinalAmount;
            fresh.Sale.RemainingAmount = 0;

            await _orderRepository.UpdateAsync(fresh);
            await _unitOfWork.SaveChangesAsync();

            // Mettre à jour l'état local
            Order = fresh;
            OnPropertyChanged(nameof(DepositAmount));
            OnPropertyChanged(nameof(RemainingAmount));
            OnPropertyChanged(nameof(HasRemainingBalance));
            (EncashBalanceCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // Créer une notification
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
                await _notificationRepository.CreateAsync(notification);
                await _unitOfWork.SaveChangesAsync();
            }

            // Signaler que la commande a été mise à jour pour rafraîchir la liste/Kanban
            OrderUpdated?.Invoke(this, fresh);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de l'encaissement : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrderDetailViewModel] Encashment error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
