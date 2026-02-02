using System;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Notifications.
/// </summary>
public class NotificationsViewModel : BaseViewModel
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    private NotificationsListViewModel _notificationsListViewModel;

    public NotificationsListViewModel NotificationsListViewModel
    {
        get => _notificationsListViewModel;
        set => SetProperty(ref _notificationsListViewModel, value);
    }

    public NotificationsViewModel(
        INotificationRepository notificationRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _notificationRepository = notificationRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;

        _notificationsListViewModel = new NotificationsListViewModel(
            notificationRepository,
            productRepository,
            unitOfWork);

        Title = "🔔 Notifications";

        // Marquer toutes les notifications comme lues au chargement
        _ = MarkAllAsReadOnLoad();
    }

    private async Task MarkAllAsReadOnLoad()
    {
        await NotificationsListViewModel.MarkAllAsReadAsync();
    }
}
