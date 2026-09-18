using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.GetOrderDetails;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Time;
using MMV.Infrastructure.Services;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche détaillée d'une commande avec workflow de statut
/// et checklist contrôle qualité.
/// </summary>
public class OrderDetailViewModel : BaseViewModel
{
    private readonly IAdvanceOrderStatusUseCase _advanceOrderStatusUseCase;
    private readonly ISettleOrderBalanceUseCase _settleOrderBalanceUseCase;

    private OrderDetailsDto? _order;
    private ObservableCollection<OrderDetailsItemDto> _items = new();

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

    public OrderDetailsDto? Order
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

    public ObservableCollection<OrderDetailsItemDto> Items
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
    public event EventHandler<OrderDetailsDto>? EditRequested;
    public event EventHandler<OrderDetailsDto>? DeleteRequested;
    public event EventHandler<OrderDetailsDto>? PrintFabSheetRequested;
    public event EventHandler<OrderDetailsDto>? OrderUpdated;

    #endregion

    // P4-5D : horloge injectable (ADR-PROD-DB-004 T1). Paramètre OPTIONNEL, à l'image du paramètre optionnel
    // déjà en usage dans les use cases : les ViewModels de ce dépôt sont construits à la main (navigation,
    // code-behind), pas résolues par le conteneur, et rendre l'horloge obligatoire aurait imposé de toucher
    // leurs sites de construction sans rien apporter au runtime. Le défaut est SystemClock.Instance —
    // exactement l'instance que le composition root enregistre — de sorte qu'il n'existe jamais deux horloges
    // dans le processus, tout en laissant un test fixer le temps.
    private readonly IClock _clock;

    public OrderDetailViewModel(
        IAdvanceOrderStatusUseCase advanceOrderStatusUseCase,
        ISettleOrderBalanceUseCase settleOrderBalanceUseCase,
        IClock? clock = null)
    {
        // Use case d'avancement de statut (P2B-2E) obligatoire : le flux d'avancement est délégué à la couche
        // Application (plus de mise à jour de statut / mouvements de stock / notification directs dans la VM).
        _advanceOrderStatusUseCase = advanceOrderStatusUseCase ?? throw new ArgumentNullException(nameof(advanceOrderStatusUseCase));
        // Use case d'encaissement du solde (P2B-2G) obligatoire : le flux d'encaissement est délégué à la couche
        // Application (plus de mise à jour du paiement / notification / SaveChanges directs dans la VM).
        // P2B-2J : IOrderRepository / IUnitOfWork / INotificationRepository retirés (dépendances mortes depuis
        // P2B-2E/P2B-2G — plus aucun accès direct au repository ni à l'unité de travail dans cette VM).
        _settleOrderBalanceUseCase = settleOrderBalanceUseCase ?? throw new ArgumentNullException(nameof(settleOrderBalanceUseCase));
        _clock = clock ?? SystemClock.Instance;

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
    public void Initialize(OrderDetailsDto order)
    {
        Order = order;
        Items = new ObservableCollection<OrderDetailsItemDto>(order.Items);
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
            // P4-5D : était DateTime.Now, et c'était FAUX — EstimatedDelivery est un instant UTC relu depuis
            // la base, que l'on soustrayait d'une heure LOCALE. Le nombre de jours restants était donc décalé
            // du décalage horaire du poste, et pouvait basculer « en retard » une à deux heures trop tôt ou
            // trop tard. Les deux termes de la soustraction sont désormais UTC (ADR-PROD-DB-004 §5, décision 6 :
            // la conversion en heure locale n'a lieu qu'au FORMATAGE d'une date affichée, jamais dans un calcul).
            var remaining = (Order.EstimatedDelivery!.Value - _clock.UtcNow).Days;
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
    /// Fait avancer le statut de la commande vers l'étape suivante en <b>déléguant</b> l'orchestration métier
    /// (mise à jour du statut, mouvements de stock de fabrication, notification, enregistrement) au use case
    /// Application <see cref="IAdvanceOrderStatusUseCase"/> (P2B-2E). La ViewModel conserve la validation/garde
    /// d'affichage (<see cref="CanAdvanceStatus"/>), construit la commande à partir de son état (statut courant,
    /// transition résolue, libellés d'affichage) et mappe le résultat vers son état local / ses événements.
    /// </summary>
    private async Task AdvanceStatusAsync()
    {
        var nextStatus = GetNextStatus();
        if (Order == null || !nextStatus.HasValue) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var command = new AdvanceOrderStatusCommand
            {
                OrderId = Order.OrderId,
                CurrentStatus = Order.Status,
                NextStatus = nextStatus.Value,
                CustomerDisplayName = CustomerName,
                CurrentStatusDisplay = OrdersListViewModel.StatusEnumToDisplay(Order.Status),
                NextStatusDisplay = OrdersListViewModel.StatusEnumToDisplay(nextStatus.Value)
            };

            var result = await _advanceOrderStatusUseCase.ExecuteAsync(command);
            if (!result.OrderFound || result.Order == null)
            {
                ErrorMessage = "Commande introuvable.";
                return;
            }

            // Mettre à jour l'état local : l'entité rechargée par le use case (inchangé, P2D-7) est projetée vers le
            // même DTO que la lecture initiale (GetOrderDetailsUseCase.MapToDto), pour garder un état cohérent.
            var updated = GetOrderDetailsUseCase.MapToDto(result.Order);
            Order = updated;
            Items = new ObservableCollection<OrderDetailsItemDto>(updated.Items);

            // Signaler que la commande a été mise à jour pour rafraîchir la liste/Kanban
            OrderUpdated?.Invoke(this, updated);
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
    /// Encaisse le montant restant d'une commande en <b>déléguant</b> l'orchestration (rechargement de la
    /// commande, mise à jour du paiement de la vente liée, notification, enregistrement transactionnel) au use
    /// case Application <see cref="ISettleOrderBalanceUseCase"/> (P2B-2G). La ViewModel conserve la validation/garde
    /// d'affichage (<see cref="HasRemainingBalance"/>), construit la commande à partir de son état (identifiant de
    /// la commande) et mappe le résultat vers son état local / ses événements.
    /// </summary>
    private async Task ExecuteEncashBalanceAsync()
    {
        if (Order == null || !HasRemainingBalance || Order.Sale == null) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var command = new SettleOrderBalanceCommand { OrderId = Order.OrderId };

            var result = await _settleOrderBalanceUseCase.ExecuteAsync(command);
            if (!result.OrderFound || result.Order == null)
            {
                ErrorMessage = "Commande introuvable.";
                return;
            }

            // Mettre à jour l'état local : même projection DTO que la lecture initiale (cf. AdvanceStatusAsync).
            var updated = GetOrderDetailsUseCase.MapToDto(result.Order);
            Order = updated;
            OnPropertyChanged(nameof(DepositAmount));
            OnPropertyChanged(nameof(RemainingAmount));
            OnPropertyChanged(nameof(HasRemainingBalance));
            (EncashBalanceCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // Signaler que la commande a été mise à jour pour rafraîchir la liste/Kanban
            OrderUpdated?.Invoke(this, updated);
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
