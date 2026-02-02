using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MMV.App.Commands;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.App.ViewModels;

/// <summary>
/// ViewModel pour la liste des notifications.
/// </summary>
public class NotificationsListViewModel : BaseViewModel
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

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
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _notificationRepository = notificationRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;

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
            var products = await _productRepository.GetAllAsync();
            var lowStockProducts = products.Where(p => p.StockQuantity <= p.StockAlertThreshold).ToList();

            foreach (var product in lowStockProducts)
            {
                // Vérifier si une notification existe déjà pour ce produit
                var existingNotifications = await _notificationRepository.GetAllAsync();
                var exists = existingNotifications.Any(n => 
                    n.Type == "LowStock" && 
                    n.EntityId == product.ProductId && 
                    !n.IsRead);

                if (!exists)
                {
                    var notification = new Notification
                    {
                        Type = "LowStock",
                        Title = $"Stock bas : {product.Name}",
                        Message = $"Le produit {product.Reference} - {product.Name} est en stock bas ({product.StockQuantity}/{product.StockAlertThreshold})",
                        EntityId = product.ProductId,
                        EntityType = "Product",
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    };

                    await _notificationRepository.CreateAsync(notification);
                }
            }

            await _unitOfWork.SaveChangesAsync();
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
            await _notificationRepository.MarkAllAsReadAsync();
            await _unitOfWork.SaveChangesAsync();
            await LoadNotificationsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur lors du marquage des notifications : {ex.Message}";
        }
    }
}
