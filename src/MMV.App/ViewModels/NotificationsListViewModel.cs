using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Notifications.CountUnreadNotifications;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.ListNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des notifications.
/// <para>
/// P2C-GLOBAL : le marquage global et la génération de notifications de stock bas passent par les use cases
/// Application.
/// P2D-3 : les lectures passent par <see cref="IListNotificationsUseCase"/> et
/// <see cref="ICountUnreadNotificationsUseCase"/> (renvoyant des DTO / un scalaire) ; plus aucune dépendance
/// <c>INotificationRepository</c>.
/// </para>
/// </summary>
public class NotificationsListViewModel : BaseViewModel
{
    private readonly IListNotificationsUseCase _listNotificationsUseCase;
    private readonly ICountUnreadNotificationsUseCase _countUnreadNotificationsUseCase;
    private readonly IMarkAllNotificationsReadUseCase _markAllNotificationsReadUseCase;
    private readonly IGenerateLowStockNotificationsUseCase _generateLowStockNotificationsUseCase;

    private ObservableCollection<NotificationListItemDto> _notifications;
    private int _unreadCount;
    private ICommand? _refreshCommand;
    private ICommand? _markAllAsReadCommand;

    public ObservableCollection<NotificationListItemDto> Notifications
    {
        get => _notifications;
        set => SetProperty(ref _notifications, value);
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set => SetProperty(ref _unreadCount, value);
    }

    public ICommand RefreshCommand => _refreshCommand ??= new RelayCommand(async () => await LoadNotificationsAsync());
    
    public ICommand MarkAllAsReadCommand => _markAllAsReadCommand ??= new RelayCommand(async () => await MarkAllAsReadAsync());

    public NotificationsListViewModel(
        IListNotificationsUseCase listNotificationsUseCase,
        ICountUnreadNotificationsUseCase countUnreadNotificationsUseCase,
        IMarkAllNotificationsReadUseCase markAllNotificationsReadUseCase,
        IGenerateLowStockNotificationsUseCase generateLowStockNotificationsUseCase)
    {
        _listNotificationsUseCase = listNotificationsUseCase ?? throw new ArgumentNullException(nameof(listNotificationsUseCase));
        _countUnreadNotificationsUseCase = countUnreadNotificationsUseCase ?? throw new ArgumentNullException(nameof(countUnreadNotificationsUseCase));
        _markAllNotificationsReadUseCase = markAllNotificationsReadUseCase ?? throw new ArgumentNullException(nameof(markAllNotificationsReadUseCase));
        _generateLowStockNotificationsUseCase = generateLowStockNotificationsUseCase ?? throw new ArgumentNullException(nameof(generateLowStockNotificationsUseCase));

        _notifications = new ObservableCollection<NotificationListItemDto>();

        _ = LoadNotificationsAsync();
    }

    public async Task LoadNotificationsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var notifications = await _listNotificationsUseCase.ExecuteAsync(new ListNotificationsQuery());
            Notifications.Clear();

            foreach (var notification in notifications)
            {
                Notifications.Add(notification);
            }

            UnreadCount = await _countUnreadNotificationsUseCase.ExecuteAsync(new CountUnreadNotificationsQuery());
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du chargement des notifications : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task GenerateLowStockNotificationsAsync()
    {
        try
        {
            await _generateLowStockNotificationsUseCase.ExecuteAsync();
            await LoadNotificationsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors de la génération des notifications : {ex.Message}";
        }
    }

    public async Task MarkAllAsReadAsync()
    {
        try
        {
            await _markAllNotificationsReadUseCase.ExecuteAsync();
            await LoadNotificationsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du marquage des notifications : {ex.Message}";
        }
    }
}
