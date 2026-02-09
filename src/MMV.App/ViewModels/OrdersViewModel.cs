using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.App.Services;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel coordinateur pour le module Commandes / Workflow Atelier.
/// Gère la navigation entre la liste, le formulaire, les détails, le Kanban et la fiche de fabrication.
/// </summary>
public class OrdersViewModel : BaseViewModel
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IProductRepository _productRepository;
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly IStockMovementRepository _stockMovementRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDialogService _dialogService;

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

    public OrdersViewModel(
        IOrderRepository orderRepository,
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        IPrescriptionRepository prescriptionRepository,
        IStockMovementRepository stockMovementRepository,
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork,
        IDialogService dialogService)
    {
        _orderRepository = orderRepository;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _prescriptionRepository = prescriptionRepository;
        _stockMovementRepository = stockMovementRepository;
        _notificationRepository = notificationRepository;
        _unitOfWork = unitOfWork;
        _dialogService = dialogService;

        // Initialiser la liste
        _listViewModel = new OrdersListViewModel(orderRepository);
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
                _orderRepository, _customerRepository, _productRepository,
                _prescriptionRepository, _unitOfWork);
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
    private async void OnEditOrderRequested(object? sender, Order order)
    {
        try
        {
            ErrorMessage = null;
            CloseDetail();

            FormViewModel = new OrderFormViewModel(
                _orderRepository, _customerRepository, _productRepository,
                _prescriptionRepository, _unitOfWork, order);
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
    private async void OnViewOrderDetail(object? sender, Order order)
    {
        try
        {
            // Recharger la commande avec ses items
            var fullOrder = await _orderRepository.GetWithItemsAsync(order.OrderId);
            if (fullOrder == null)
            {
                ErrorMessage = "Commande introuvable.";
                return;
            }

            DetailViewModel = new OrderDetailViewModel(
                _orderRepository, _unitOfWork, _stockMovementRepository, _notificationRepository);
            DetailViewModel.Initialize(fullOrder);
            DetailViewModel.BackRequested += OnDetailBackRequested;
            DetailViewModel.EditRequested += OnEditOrderRequested;
            DetailViewModel.DeleteRequested += OnDeleteOrderRequested;
            DetailViewModel.PrintFabSheetRequested += OnPrintFabSheetRequested;

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
                KanbanViewModel = new OrderKanbanViewModel(_orderRepository, _unitOfWork);
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
    private void OnPrintFabSheetRequested(object? sender, Order order)
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

    private async void OnDeleteOrderRequested(object? sender, Order order)
    {
        if (order == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "Confirmation de suppression",
            $"Êtes-vous sûr de vouloir supprimer la commande {order.OrderNumber} ?\n\n⚠️ Cette action est irréversible !");

        if (confirmed)
        {
            try
            {
                await _orderRepository.DeleteAsync(order.OrderId);
                await _unitOfWork.SaveChangesAsync();
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

    #endregion

    private void CloseDetail()
    {
        IsShowingDetail = false;
        DetailViewModel = null;
    }
}
