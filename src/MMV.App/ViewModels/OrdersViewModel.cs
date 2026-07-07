using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Application.UseCases.Customers.ListCustomersForPicker;
using MMV.Application.UseCases.Orders.AdvanceOrderStatus;
using MMV.Application.UseCases.Orders.CreateOrder;
using MMV.Application.UseCases.Orders.DeleteOrder;
using MMV.Application.UseCases.Orders.GetOrderDetails;
using MMV.Application.UseCases.Orders.ListOrders;
using MMV.Application.UseCases.Orders.SettleOrderBalance;
using MMV.Application.UseCases.Orders.UpdateOrder;
using MMV.Application.UseCases.Prescriptions.ListPrescriptionsByCustomer;
using MMV.Application.UseCases.Products.ListProductsForOrderPicker;
using MMV.Domain.Interfaces.Persistence;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel coordinateur pour le module Commandes / Workflow Atelier.
/// Gère la navigation entre la liste, le formulaire, les détails, le Kanban et la fiche de fabrication.
/// </summary>
public class OrdersViewModel : BaseViewModel
{
    private readonly IGetOrderDetailsUseCase _getOrderDetailsUseCase;
    private readonly IListOrdersUseCase _listOrdersUseCase;
    private readonly IListCustomersForPickerUseCase _listCustomersUseCase;
    private readonly IListProductsForOrderPickerUseCase _listProductsUseCase;
    private readonly IListPrescriptionsByCustomerUseCase _listPrescriptionsUseCase;
    private readonly IDialogService _dialogService;
    private readonly INumberSequenceService _numberSequenceService;
    private readonly ICreateOrderUseCase _createOrderUseCase;
    private readonly IUpdateOrderUseCase _updateOrderUseCase;
    private readonly IAdvanceOrderStatusUseCase _advanceOrderStatusUseCase;
    private readonly ISettleOrderBalanceUseCase _settleOrderBalanceUseCase;
    private readonly IDeleteOrderUseCase _deleteOrderUseCase;

    private OrdersListViewModel _listViewModel;
    private OrderFormViewModel? _formViewModel;
    private OrderDetailViewModel? _detailViewModel;
    private OrderKanbanViewModel? _kanbanViewModel;
    private FabricationSheetViewModel? _fabricationSheetViewModel;

    private bool _isInEditMode;
    private bool _isShowingDetail;
    private bool _isShowingKanban;
    private bool _isShowingFabricationSheet;

    #region Properties

    public OrdersListViewModel ListViewModel
    {
        get => _listViewModel;
        set => SetProperty(ref _listViewModel, value);
    }

    public OrderFormViewModel? FormViewModel
    {
        get => _formViewModel;
        set => SetProperty(ref _formViewModel, value);
    }

    public OrderDetailViewModel? DetailViewModel
    {
        get => _detailViewModel;
        set => SetProperty(ref _detailViewModel, value);
    }

    public OrderKanbanViewModel? KanbanViewModel
    {
        get => _kanbanViewModel;
        set => SetProperty(ref _kanbanViewModel, value);
    }

    public FabricationSheetViewModel? FabricationSheetViewModel
    {
        get => _fabricationSheetViewModel;
        set => SetProperty(ref _fabricationSheetViewModel, value);
    }

    public bool IsInEditMode
    {
        get => _isInEditMode;
        set
        {
            if (SetProperty(ref _isInEditMode, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool IsShowingDetail
    {
        get => _isShowingDetail;
        set
        {
            if (SetProperty(ref _isShowingDetail, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool IsShowingKanban
    {
        get => _isShowingKanban;
        set
        {
            if (SetProperty(ref _isShowingKanban, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    public bool IsShowingFabricationSheet
    {
        get => _isShowingFabricationSheet;
        set
        {
            if (SetProperty(ref _isShowingFabricationSheet, value))
                OnPropertyChanged(nameof(ShowList));
        }
    }

    /// <summary>
    /// Affiche la liste uniquement quand aucune sous-vue n'est active.
    /// </summary>
    public bool ShowList => !IsInEditMode && !IsShowingDetail && !IsShowingKanban && !IsShowingFabricationSheet;

    #endregion

    // P2C-GLOBAL : le paramètre IUnitOfWork a été supprimé — après le passage de l'avancement Kanban à
    // IAdvanceOrderStatusUseCase, il n'était plus utilisé (dépendance morte).
    // P2D-6 : les lectures de référence du formulaire de commande (clients, produits, ordonnances) passent par des
    // query use cases Application (transmis à OrderFormViewModel) — les trois repositories ICustomer/IProduct/
    // IPrescription ont été retirés.
    // P2D-7B : IOrderRepository remplacé par IGetOrderDetailsUseCase pour le rechargement de la fiche détaillée, qui
    // alimente OrderDetailViewModel puis le formulaire d'ÉDITION avec un OrderDetailsDto (jamais l'entité EF Order).
    public OrdersViewModel(
        IGetOrderDetailsUseCase getOrderDetailsUseCase,
        IListOrdersUseCase listOrdersUseCase,
        IListCustomersForPickerUseCase listCustomersUseCase,
        IListProductsForOrderPickerUseCase listProductsUseCase,
        IListPrescriptionsByCustomerUseCase listPrescriptionsUseCase,
        IDialogService dialogService,
        INumberSequenceService numberSequenceService,
        ICreateOrderUseCase createOrderUseCase,
        IUpdateOrderUseCase updateOrderUseCase,
        IAdvanceOrderStatusUseCase advanceOrderStatusUseCase,
        ISettleOrderBalanceUseCase settleOrderBalanceUseCase,
        IDeleteOrderUseCase deleteOrderUseCase)
    {
        _getOrderDetailsUseCase = getOrderDetailsUseCase ?? throw new ArgumentNullException(nameof(getOrderDetailsUseCase));
        _listOrdersUseCase = listOrdersUseCase ?? throw new ArgumentNullException(nameof(listOrdersUseCase));
        _listCustomersUseCase = listCustomersUseCase ?? throw new ArgumentNullException(nameof(listCustomersUseCase));
        _listProductsUseCase = listProductsUseCase ?? throw new ArgumentNullException(nameof(listProductsUseCase));
        _listPrescriptionsUseCase = listPrescriptionsUseCase ?? throw new ArgumentNullException(nameof(listPrescriptionsUseCase));
        _dialogService = dialogService;
        // Numérotation fiable obligatoire (P2A-1E) : injectée par DI, transmise jusqu'à OrderFormViewModel.
        _numberSequenceService = numberSequenceService ?? throw new ArgumentNullException(nameof(numberSequenceService));
        // Use case de création (P2B-2D) obligatoire : transmis jusqu'à OrderFormViewModel pour la délégation.
        _createOrderUseCase = createOrderUseCase ?? throw new ArgumentNullException(nameof(createOrderUseCase));
        // Use case d'édition (P2B-2I) obligatoire : transmis jusqu'à OrderFormViewModel pour la délégation du
        // flux de modification d'une commande existante.
        _updateOrderUseCase = updateOrderUseCase ?? throw new ArgumentNullException(nameof(updateOrderUseCase));
        // Use case d'avancement de statut (P2B-2E) obligatoire : transmis jusqu'à OrderDetailViewModel.
        _advanceOrderStatusUseCase = advanceOrderStatusUseCase ?? throw new ArgumentNullException(nameof(advanceOrderStatusUseCase));
        // Use case d'encaissement du solde (P2B-2G) obligatoire : transmis jusqu'à OrderDetailViewModel.
        _settleOrderBalanceUseCase = settleOrderBalanceUseCase ?? throw new ArgumentNullException(nameof(settleOrderBalanceUseCase));
        // Use case de suppression de commande (P2B-2H) obligatoire : le flux de suppression est délégué à la
        // couche Application (plus de DeleteAsync/SaveChangesAsync directs dans la VM).
        _deleteOrderUseCase = deleteOrderUseCase ?? throw new ArgumentNullException(nameof(deleteOrderUseCase));

        // Initialiser la liste
        _listViewModel = new OrdersListViewModel(_listOrdersUseCase);
        _listViewModel.CreateOrderRequested += OnCreateOrderRequested;
        _listViewModel.ViewOrderDetailRequested += OnViewOrderDetail;
        _listViewModel.ShowKanbanRequested += OnShowKanban;

        Title = "🏭 Gestion des Commandes";
    }

    #region Navigation Handlers

    /// <summary>
    /// Ouvre le formulaire de création d'une nouvelle commande.
    /// </summary>
    private async void OnCreateOrderRequested(object? sender, EventArgs e)
    {
        try
        {
            ErrorMessage = null;
            FormViewModel = new OrderFormViewModel(
                _listCustomersUseCase, _listProductsUseCase, _listPrescriptionsUseCase,
                _numberSequenceService, _createOrderUseCase, _updateOrderUseCase);
            FormViewModel.OrderSaved += OnOrderSaved;
            FormViewModel.CancelRequested += OnFormCancelled;

            await FormViewModel.InitializeAsync();
            IsInEditMode = true;
        }
        catch (Exception ex)
        {
            IsInEditMode = false;
            FormViewModel = null;
            ErrorMessage = $"Erreur lors de l'ouverture du formulaire : {ex.Message}";
        }
    }

    /// <summary>
    /// Ouvre le formulaire d'édition pour une commande existante.
    /// </summary>
    private async void OnEditOrderRequested(object? sender, OrderDetailsDto order)
    {
        try
        {
            ErrorMessage = null;
            CloseDetail();

            FormViewModel = new OrderFormViewModel(
                _listCustomersUseCase, _listProductsUseCase, _listPrescriptionsUseCase,
                _numberSequenceService, _createOrderUseCase, _updateOrderUseCase, order);
            FormViewModel.OrderSaved += OnOrderSaved;
            FormViewModel.CancelRequested += OnFormCancelled;

            await FormViewModel.InitializeAsync();
            IsInEditMode = true;
        }
        catch (Exception ex)
        {
            IsInEditMode = false;
            FormViewModel = null;
            ErrorMessage = $"Erreur lors de l'ouverture du formulaire : {ex.Message}";
        }
    }

    /// <summary>
    /// Affiche la fiche détaillée d'une commande.
    /// </summary>
    private async void OnViewOrderDetail(object? sender, OrderListItemDto order)
    {
        try
        {
            // Recharger la commande avec ses items. P2D-6 : la liste / le Kanban fournissent un DTO (OrderListItemDto) ;
            // seul l'identifiant est utilisé pour recharger la fiche détaillée complète. P2D-7B : le rechargement
            // passe par IGetOrderDetailsUseCase (OrderDetailsDto), plus IOrderRepository direct.
            var fullOrder = await _getOrderDetailsUseCase.ExecuteAsync(new GetOrderDetailsQuery { OrderId = order.OrderId });
            if (fullOrder == null)
            {
                ErrorMessage = "Commande introuvable.";
                return;
            }

            DetailViewModel = new OrderDetailViewModel(
                _advanceOrderStatusUseCase, _settleOrderBalanceUseCase);
            DetailViewModel.Initialize(fullOrder);
            DetailViewModel.BackRequested += OnDetailBackRequested;
            DetailViewModel.EditRequested += OnEditOrderRequested;
            DetailViewModel.DeleteRequested += OnDeleteOrderRequested;
            DetailViewModel.PrintFabSheetRequested += OnPrintFabSheetRequested;
            DetailViewModel.OrderUpdated += OnOrderUpdated;

            IsShowingKanban = false;
            IsShowingDetail = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement de la commande : {ex.Message}";
        }
    }

    /// <summary>
    /// Affiche la vue Kanban.
    /// </summary>
    private async void OnShowKanban(object? sender, EventArgs e)
    {
        try
        {
            if (KanbanViewModel == null)
            {
                KanbanViewModel = new OrderKanbanViewModel(_listOrdersUseCase, _advanceOrderStatusUseCase);
                KanbanViewModel.ViewOrderDetailRequested += OnViewOrderDetail;
                KanbanViewModel.BackToListRequested += OnKanbanBackToList;
            }

            await KanbanViewModel.LoadOrdersAsync();
            IsShowingKanban = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur Kanban : {ex.Message}";
        }
    }

    /// <summary>
    /// Affiche la fiche de fabrication.
    /// </summary>
    private void OnPrintFabSheetRequested(object? sender, OrderDetailsDto order)
    {
        FabricationSheetViewModel = new FabricationSheetViewModel();
        FabricationSheetViewModel.Initialize(order);
        FabricationSheetViewModel.BackRequested += OnFabSheetBackRequested;

        IsShowingDetail = false;
        IsShowingFabricationSheet = true;
    }

    #endregion

    #region Callback Handlers

    private async void OnOrderSaved(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        FormViewModel = null;
        await ListViewModel.LoadOrdersAsync();
    }

    private void OnFormCancelled(object? sender, EventArgs e)
    {
        IsInEditMode = false;
        FormViewModel = null;
    }

    private void OnDetailBackRequested(object? sender, EventArgs e)
    {
        CloseDetail();
    }

    private async void OnDeleteOrderRequested(object? sender, OrderDetailsDto order)
    {
        if (order == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Confirmation de suppression",
            $"Êtes-vous sûr de vouloir supprimer la commande {order.OrderNumber} ?\n\n⚠️ Cette action est irréversible !");

        if (confirmed)
        {
            try
            {
                var command = new DeleteOrderCommand { OrderId = order.OrderId };
                await _deleteOrderUseCase.ExecuteAsync(command);
                CloseDetail();
                await ListViewModel.LoadOrdersAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Erreur de suppression : {ex.Message}";
                await _dialogService.ShowErrorAsync("Erreur", $"Impossible de supprimer la commande :\n{ex.Message}");
            }
        }
    }

    private void OnKanbanBackToList(object? sender, EventArgs e)
    {
        IsShowingKanban = false;
    }

    private void OnFabSheetBackRequested(object? sender, EventArgs e)
    {
        IsShowingFabricationSheet = false;
        FabricationSheetViewModel = null;
    }

    /// <summary>
    /// Rafraîchit discrètement la liste et le Kanban après une mise à jour de commande.
    /// </summary>
    private async void OnOrderUpdated(object? sender, OrderDetailsDto order)
    {
        // Rafraîchir la liste en arrière-plan
        await ListViewModel.LoadOrdersAsync();

        // Rafraîchir le Kanban s'il est visible
        if (KanbanViewModel != null)
        {
            await KanbanViewModel.LoadOrdersAsync();
        }
    }

    #endregion

    private void CloseDetail()
    {
        IsShowingDetail = false;
        DetailViewModel = null;
    }
}
