using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la vue Kanban des commandes.
/// Organise les commandes en colonnes par statut de workflow.
/// <para>
/// P2C-GLOBAL : l'avancement de statut passe désormais par <see cref="IAdvanceOrderStatusUseCase"/> (comme
/// <c>OrderDetailViewModel</c>), alignant les deux surfaces UI sur la même orchestration applicative.
/// <see cref="IOrderRepository"/> n'est conservé que pour les lectures d'affichage (répartition des colonnes) ;
/// <c>IUnitOfWork</c> a été retiré.
/// </para>
/// </summary>
public class OrderKanbanViewModel : BaseViewModel
{
    private readonly IOrderRepository _orderRepository;
    private readonly IAdvanceOrderStatusUseCase _advanceOrderStatusUseCase;

    private ObservableCollection<Order> _newOrders = new();
    private ObservableCollection<Order> _toFabricateOrders = new();
    private ObservableCollection<Order> _inProgressOrders = new();
    private ObservableCollection<Order> _qualityCheckOrders = new();
    private ObservableCollection<Order> _readyOrders = new();
    private ObservableCollection<Order> _deliveredOrders = new();

    #region Properties

    public ObservableCollection<Order> NewOrders
    {
        get => _newOrders;
        set => SetProperty(ref _newOrders, value);
    }

    public ObservableCollection<Order> ToFabricateOrders
    {
        get => _toFabricateOrders;
        set => SetProperty(ref _toFabricateOrders, value);
    }

    public ObservableCollection<Order> InProgressOrders
    {
        get => _inProgressOrders;
        set => SetProperty(ref _inProgressOrders, value);
    }

    public ObservableCollection<Order> QualityCheckOrders
    {
        get => _qualityCheckOrders;
        set => SetProperty(ref _qualityCheckOrders, value);
    }

    public ObservableCollection<Order> ReadyOrders
    {
        get => _readyOrders;
        set => SetProperty(ref _readyOrders, value);
    }

    public ObservableCollection<Order> DeliveredOrders
    {
        get => _deliveredOrders;
        set => SetProperty(ref _deliveredOrders, value);
    }

    public int NewCount => NewOrders.Count;
    public int ToFabricateCount => ToFabricateOrders.Count;
    public int InProgressCount => InProgressOrders.Count;
    public int QualityCheckCount => QualityCheckOrders.Count;
    public int ReadyCount => ReadyOrders.Count;
    public int DeliveredCount => DeliveredOrders.Count;

    #endregion

    #region Commands

    public ICommand AdvanceStatusCommand { get; }
    public ICommand ViewDetailCommand { get; }
    public ICommand BackToListCommand { get; }
    public ICommand RefreshCommand { get; }

    #endregion

    #region Events

    public event EventHandler<Order>? ViewOrderDetailRequested;
    public event EventHandler? BackToListRequested;

    #endregion

    public OrderKanbanViewModel(IOrderRepository orderRepository, IAdvanceOrderStatusUseCase advanceOrderStatusUseCase)
    {
        _orderRepository = orderRepository;
        _advanceOrderStatusUseCase = advanceOrderStatusUseCase ?? throw new ArgumentNullException(nameof(advanceOrderStatusUseCase));

        AdvanceStatusCommand = new RelayCommand<Order>(async (o) => await AdvanceStatusAsync(o));
        ViewDetailCommand = new RelayCommand<Order>(o =>
        {
            if (o != null) ViewOrderDetailRequested?.Invoke(this, o);
        });
        BackToListCommand = new RelayCommand(() => BackToListRequested?.Invoke(this, EventArgs.Empty));
        RefreshCommand = new RelayCommand(async () => await LoadOrdersAsync());

        Title = "Vue Kanban";
    }

    /// <summary>
    /// Charge toutes les commandes et les répartit par colonnes.
    /// </summary>
    public async Task LoadOrdersAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var orders = await _orderRepository.GetAllWithItemsAsync();

            NewOrders = new ObservableCollection<Order>(
                orders.Where(o => o.Status == OrderStatus.New).OrderByDescending(o => o.OrderDate));
            ToFabricateOrders = new ObservableCollection<Order>(
                orders.Where(o => o.Status == OrderStatus.ToFabricate).OrderByDescending(o => o.OrderDate));
            InProgressOrders = new ObservableCollection<Order>(
                orders.Where(o => o.Status == OrderStatus.InProgress).OrderByDescending(o => o.OrderDate));
            QualityCheckOrders = new ObservableCollection<Order>(
                orders.Where(o => o.Status == OrderStatus.QualityCheck).OrderByDescending(o => o.OrderDate));
            ReadyOrders = new ObservableCollection<Order>(
                orders.Where(o => o.Status == OrderStatus.Ready).OrderByDescending(o => o.OrderDate));
            DeliveredOrders = new ObservableCollection<Order>(
                orders.Where(o => o.Status == OrderStatus.Delivered).OrderByDescending(o => o.OrderDate).Take(10));

            NotifyCounts();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrderKanbanViewModel] Error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Fait avancer le statut d'une commande d'un cran.
    /// </summary>
    private async Task AdvanceStatusAsync(Order? order)
    {
        if (order == null) return;

        var next = GetNextStatus(order.Status);
        if (!next.HasValue) return;

        try
        {
            var customerName = order.Sale?.Customer != null
                ? $"{order.Sale.Customer.FirstName} {order.Sale.Customer.LastName}"
                : "Client inconnu";

            await _advanceOrderStatusUseCase.ExecuteAsync(new AdvanceOrderStatusCommand
            {
                OrderId = order.OrderId,
                CurrentStatus = order.Status,
                NextStatus = next.Value,
                CustomerDisplayName = customerName,
                CurrentStatusDisplay = OrdersListViewModel.StatusEnumToDisplay(order.Status),
                NextStatusDisplay = OrdersListViewModel.StatusEnumToDisplay(next.Value)
            });

            await LoadOrdersAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrderKanbanViewModel] Advance error: {ex}");
        }
    }

    private static OrderStatus? GetNextStatus(OrderStatus current)
    {
        return current switch
        {
            OrderStatus.New => OrderStatus.ToFabricate,
            OrderStatus.ToFabricate => OrderStatus.InProgress,
            OrderStatus.InProgress => OrderStatus.QualityCheck,
            OrderStatus.QualityCheck => OrderStatus.Ready,
            OrderStatus.Ready => OrderStatus.Delivered,
            _ => null
        };
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(NewCount));
        OnPropertyChanged(nameof(ToFabricateCount));
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(QualityCheckCount));
        OnPropertyChanged(nameof(ReadyCount));
        OnPropertyChanged(nameof(DeliveredCount));
    }
}
