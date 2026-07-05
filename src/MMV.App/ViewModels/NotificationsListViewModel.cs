using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;
using MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des notifications.
/// <para>
/// P2C-GLOBAL : le marquage global et la génération de notifications de stock bas passent par les use cases
/// Application. <see cref="INotificationRepository"/> n'est conservé que pour les lectures d'affichage
/// (<c>GetAllAsync</c>, <c>CountUnreadAsync</c>) ; <c>IProductRepository</c> et <c>IUnitOfWork</c> ont été retirés.
/// </para>
/// </summary>
public class NotificationsListViewModel : BaseViewModel
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IMarkAllNotificationsReadUseCase _markAllNotificationsReadUseCase;
    private readonly IGenerateLowStockNotificationsUseCase _generateLowStockNotificationsUseCase;

    private ObservableCollection<Notification> _notifications;
    private int _unreadCount;
    private ICommand? _refreshCommand;
    private ICommand? _markAllAsReadCommand;

    public ObservableCollection<Notification> Notifications
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
        INotificationRepository notificationRepository,
        IMarkAllNotificationsReadUseCase markAllNotificationsReadUseCase,
        IGenerateLowStockNotificationsUseCase generateLowStockNotificationsUseCase)
    {
        _notificationRepository = notificationRepository;
        _markAllNotificationsReadUseCase = markAllNotificationsReadUseCase ?? throw new ArgumentNullException(nameof(markAllNotificationsReadUseCase));
        _generateLowStockNotificationsUseCase = generateLowStockNotificationsUseCase ?? throw new ArgumentNullException(nameof(generateLowStockNotificationsUseCase));

        _notifications = new ObservableCollection<Notification>();

        _ = LoadNotificationsAsync();
    }

    public async Task LoadNotificationsAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var notifications = await _notificationRepository.GetAllAsync();
            Notifications.Clear();
            
            foreach (var notification in notifications.OrderByDescending(n => n.CreatedAt))
            {
                Notifications.Add(notification);
            }

            UnreadCount = await _notificationRepository.CountUnreadAsync();
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
