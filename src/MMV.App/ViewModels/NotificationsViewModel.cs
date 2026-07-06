using System;
using System.Threading.Tasks;
using MMV.Application.UseCases.Notifications.CountUnreadNotifications;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel principal pour le module Notifications.
/// <para>
/// P2C-GLOBAL : les écritures sont déléguées aux use cases Application.
/// P2D-3 : les lectures (liste + compteur non lus) passent par des query use cases ; plus aucune dépendance
/// <c>INotificationRepository</c>.
/// </para>
/// </summary>
public class NotificationsViewModel : BaseViewModel
{
    private readonly IListNotificationsUseCase _listNotificationsUseCase;
    private readonly ICountUnreadNotificationsUseCase _countUnreadNotificationsUseCase;
    private readonly IMarkAllNotificationsReadUseCase _markAllNotificationsReadUseCase;
    private readonly IGenerateLowStockNotificationsUseCase _generateLowStockNotificationsUseCase;

    private NotificationsListViewModel _notificationsListViewModel;

    public NotificationsListViewModel NotificationsListViewModel
    {
        get => _notificationsListViewModel;
        set => SetProperty(ref _notificationsListViewModel, value);
    }

    public NotificationsViewModel(
        IListNotificationsUseCase listNotificationsUseCase,
        ICountUnreadNotificationsUseCase countUnreadNotificationsUseCase,
        IMarkAllNotificationsReadUseCase markAllNotificationsReadUseCase,
        IGenerateLowStockNotificationsUseCase generateLowStockNotificationsUseCase)
    {
        _listNotificationsUseCase = listNotificationsUseCase ?? throw new ArgumentNullException(nameof(listNotificationsUseCase));
        _countUnreadNotificationsUseCase = countUnreadNotificationsUseCase ?? throw new ArgumentNullException(nameof(countUnreadNotificationsUseCase));
        _markAllNotificationsReadUseCase = markAllNotificationsReadUseCase ?? throw new ArgumentNullException(nameof(markAllNotificationsReadUseCase));
        _generateLowStockNotificationsUseCase = generateLowStockNotificationsUseCase ?? throw new ArgumentNullException(nameof(generateLowStockNotificationsUseCase));

        _notificationsListViewModel = new NotificationsListViewModel(
            listNotificationsUseCase,
            countUnreadNotificationsUseCase,
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
