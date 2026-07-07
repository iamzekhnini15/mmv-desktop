using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Orders.ListOrders;
using MMV.Domain.Enums;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des commandes avec filtres par statut et recherche.
/// <para>
/// P2D-6 : la lecture directe <c>IOrderRepository.GetAllWithItemsAsync</c> est remplacée par le query use case
/// <see cref="IListOrdersUseCase"/> renvoyant des <see cref="OrderListItemDto"/> plats. Aucune entité EF suivie ne
/// franchit plus la frontière UI pour cet écran ; les filtres / recherche / pagination restent en présentation
/// (iso-fonctionnel).
/// </para>
/// </summary>
public class OrdersListViewModel : BaseViewModel
{
    private readonly IListOrdersUseCase _listOrdersUseCase;

    private ObservableCollection<OrderListItemDto> _orders = new();
    private ObservableCollection<OrderListItemDto> _filteredOrders = new();
    private string _searchText = string.Empty;
    private string _selectedStatus = "Tous les statuts";
    private int _currentPage = 1;
    private int _pageSize = 20;
    private int _totalOrders;

    public ObservableCollection<OrderListItemDto> Orders
    {
        get => _orders;
        set => SetProperty(ref _orders, value);
    }

    public ObservableCollection<OrderListItemDto> FilteredOrders
    {
        get => _filteredOrders;
        set => SetProperty(ref _filteredOrders, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                CurrentPage = 1;
                ApplyFilter();
            }
        }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetProperty(ref _selectedStatus, value))
            {
                CurrentPage = 1;
                ApplyFilter();
            }
        }
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
            }
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set => SetProperty(ref _pageSize, value);
    }

    public int TotalOrders
    {
        get => _totalOrders;
        set
        {
            if (SetProperty(ref _totalOrders, value))
                OnPropertyChanged(nameof(TotalPages));
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalOrders / PageSize));
    public bool CanGoToPreviousPage => CurrentPage > 1;
    public bool CanGoToNextPage => CurrentPage < TotalPages;

    public ObservableCollection<string> StatusFilters { get; } = new()
    {
        "Tous les statuts",
        "Nouveau",
        "À fabriquer",
        "En fabrication",
        "Contrôle qualité",
        "Prêt",
        "Livré"
    };

    public ICommand CreateCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ViewDetailCommand { get; }
    public ICommand ShowKanbanCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }

    public event EventHandler? CreateOrderRequested;
    public event EventHandler<OrderListItemDto>? ViewOrderDetailRequested;
    public event EventHandler? ShowKanbanRequested;

    public OrdersListViewModel(IListOrdersUseCase listOrdersUseCase)
    {
        _listOrdersUseCase = listOrdersUseCase ?? throw new ArgumentNullException(nameof(listOrdersUseCase));

        CreateCommand = new RelayCommand(() => CreateOrderRequested?.Invoke(this, EventArgs.Empty));
        RefreshCommand = new RelayCommand(async () => await LoadOrdersAsync());
        ViewDetailCommand = new RelayCommand<OrderListItemDto>(ExecuteViewDetail);
        ShowKanbanCommand = new RelayCommand(() => ShowKanbanRequested?.Invoke(this, EventArgs.Empty));
        PreviousPageCommand = new RelayCommand(() => { if (CanGoToPreviousPage) CurrentPage--; }, () => CanGoToPreviousPage);
        NextPageCommand = new RelayCommand(() => { if (CanGoToNextPage) CurrentPage++; }, () => CanGoToNextPage);

        Title = "Commandes";

        _ = LoadOrdersAsync();
    }

    public async Task LoadOrdersAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var orders = await _listOrdersUseCase.ExecuteAsync(new ListOrdersQuery());
            Orders = new ObservableCollection<OrderListItemDto>(orders);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des commandes : {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[OrdersListViewModel] Error: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        if (Orders == null) return;

        var filtered = Orders.AsEnumerable();

        // Filtre par statut
        if (!string.IsNullOrEmpty(SelectedStatus) && SelectedStatus != "Tous les statuts")
        {
            var status = StatusDisplayToEnum(SelectedStatus);
            if (status.HasValue)
            {
                filtered = filtered.Where(o => o.Status == status.Value);
            }
        }

        // Filtre par texte
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            filtered = filtered.Where(o =>
                (o.OrderNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Sale?.Customer?.FirstName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Sale?.Customer?.LastName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var filteredList = filtered.ToList();
        TotalOrders = filteredList.Count;

        // Pagination
        var paginated = filteredList
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        FilteredOrders = new ObservableCollection<OrderListItemDto>(paginated);

        OnPropertyChanged(nameof(CanGoToPreviousPage));
        OnPropertyChanged(nameof(CanGoToNextPage));
        (PreviousPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteViewDetail(OrderListItemDto? order)
    {
        if (order != null)
            ViewOrderDetailRequested?.Invoke(this, order);
    }

    /// <summary>
    /// Convertit un libellé français de statut en valeur d'enum.
    /// </summary>
    private static OrderStatus? StatusDisplayToEnum(string display)
    {
        return display switch
        {
            "Nouveau" => OrderStatus.New,
            "À fabriquer" => OrderStatus.ToFabricate,
            "En fabrication" => OrderStatus.InProgress,
            "Contrôle qualité" => OrderStatus.QualityCheck,
            "Prêt" => OrderStatus.Ready,
            "Livré" => OrderStatus.Delivered,
            _ => null
        };
    }

    /// <summary>
    /// Retourne le libellé français d'un statut.
    /// </summary>
    public static string StatusEnumToDisplay(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.New => "Nouveau",
            OrderStatus.ToFabricate => "À fabriquer",
            OrderStatus.InProgress => "En fabrication",
            OrderStatus.QualityCheck => "Contrôle qualité",
            OrderStatus.Ready => "Prêt",
            OrderStatus.Delivered => "Livré",
            _ => status.ToString()
        };
    }
}
