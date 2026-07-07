using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Orders.GetOrderDetails;
using MMV.Domain.Enums;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la fiche de fabrication imprimable (bon atelier).
/// </summary>
public class FabricationSheetViewModel : BaseViewModel
{
    private OrderDetailsDto? _order;
    private ObservableCollection<OrderDetailsItemDto> _items = new();

    #region Properties

    public OrderDetailsDto? Order
    {
        get => _order;
        set
        {
            if (SetProperty(ref _order, value))
            {
                OnPropertyChanged(nameof(OrderNumber));
                OnPropertyChanged(nameof(OrderDate));
                OnPropertyChanged(nameof(EstimatedDelivery));
                OnPropertyChanged(nameof(CustomerName));
                OnPropertyChanged(nameof(CustomerPhone));
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(Notes));
                OnPropertyChanged(nameof(TotalAmount));
                OnPropertyChanged(nameof(FrameItem));
                OnPropertyChanged(nameof(LensOdItem));
                OnPropertyChanged(nameof(LensOgItem));
                OnPropertyChanged(nameof(Accessories));
                OnPropertyChanged(nameof(HasFrame));
                OnPropertyChanged(nameof(HasLensOd));
                OnPropertyChanged(nameof(HasLensOg));
                OnPropertyChanged(nameof(HasAccessories));
            }
        }
    }

    public ObservableCollection<OrderDetailsItemDto> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    // Infos commande
    public string OrderNumber => Order?.OrderNumber ?? string.Empty;
    public DateTime OrderDate => Order?.OrderDate ?? DateTime.MinValue;
    public DateTime? EstimatedDelivery => Order?.EstimatedDelivery;
    public string StatusDisplay => OrdersListViewModel.StatusEnumToDisplay(Order?.Status ?? OrderStatus.New);
    public string Notes => Order?.Notes ?? string.Empty;
    public decimal TotalAmount => Order?.Sale?.FinalAmount ?? 0m;

    // Infos client
    public string CustomerName => Order?.Sale?.Customer != null
        ? $"{Order.Sale.Customer.FirstName} {Order.Sale.Customer.LastName}"
        : "—";
    public string CustomerPhone => Order?.Sale?.Customer?.Phone ?? "—";

    // Articles par type
    public OrderDetailsItemDto? FrameItem => Order?.Items?.FirstOrDefault(i => i.ItemType == OrderItemType.Frame);
    public OrderDetailsItemDto? LensOdItem => Order?.Items?.FirstOrDefault(i => i.ItemType == OrderItemType.LensOd);
    public OrderDetailsItemDto? LensOgItem => Order?.Items?.FirstOrDefault(i => i.ItemType == OrderItemType.LensOg);
    public OrderDetailsItemDto[] Accessories => Order?.Items?.Where(i => i.ItemType == OrderItemType.Accessory).ToArray()
                                      ?? Array.Empty<OrderDetailsItemDto>();

    public bool HasFrame => FrameItem != null;
    public bool HasLensOd => LensOdItem != null;
    public bool HasLensOg => LensOgItem != null;
    public bool HasAccessories => Accessories.Length > 0;

    #endregion

    #region Commands

    public ICommand BackCommand { get; }

    #endregion

    #region Events

    public event EventHandler? BackRequested;

    #endregion

    public FabricationSheetViewModel()
    {
        BackCommand = new RelayCommand(() => BackRequested?.Invoke(this, EventArgs.Empty));
        Title = "Fiche de Fabrication";
    }

    /// <summary>
    /// Initialise la fiche avec une commande.
    /// </summary>
    public void Initialize(OrderDetailsDto order)
    {
        Order = order;
        Items = new ObservableCollection<OrderDetailsItemDto>(order.Items);
    }
}
