using System;
using System.Threading.Tasks;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Notifications.
/// <para>
/// P2C-GLOBAL : les écritures sont déléguées aux use cases Application. <see cref="INotificationRepository"/> n'est
/// conservé que pour alimenter la liste enfant en lectures d'affichage ; <c>IProductRepository</c> et
/// <c>IUnitOfWork</c> ont été retirés.
/// </para>
/// </summary>
public class NotificationsViewModel : BaseViewModel
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IMarkAllNotificationsReadUseCase _markAllNotificationsReadUseCase;
    private readonly IGenerateLowStockNotificationsUseCase _generateLowStockNotificationsUseCase;

    private NotificationsListViewModel _notificationsListViewModel;

    public NotificationsListViewModel NotificationsListViewModel
    {
        get => _notificationsListViewModel;
        set => SetProperty(ref _notificationsListViewModel, value);
    }

    public NotificationsViewModel(
        INotificationRepository notificationRepository,
        IMarkAllNotificationsReadUseCase markAllNotificationsReadUseCase,
        IGenerateLowStockNotificationsUseCase generateLowStockNotificationsUseCase)
    {
        _notificationRepository = notificationRepository;
        _markAllNotificationsReadUseCase = markAllNotificationsReadUseCase ?? throw new ArgumentNullException(nameof(markAllNotificationsReadUseCase));
        _generateLowStockNotificationsUseCase = generateLowStockNotificationsUseCase ?? throw new ArgumentNullException(nameof(generateLowStockNotificationsUseCase));

        _notificationsListViewModel = new NotificationsListViewModel(
            notificationRepository,
            markAllNotificationsReadUseCase,
            generateLowStockNotificationsUseCase);

        Title = "🔔 Notifications";

        // Marquer toutes les notifications comme lues au chargement
        _ = MarkAllAsReadOnLoad();
    }

    private async Task MarkAllAsReadOnLoad()
    {
        await NotificationsListViewModel.MarkAllAsReadAsync();
    }
}
