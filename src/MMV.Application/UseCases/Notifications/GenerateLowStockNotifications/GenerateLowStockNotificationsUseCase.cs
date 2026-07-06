using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Notifications.GenerateLowStockNotifications;

/// <summary>
/// Implémentation du use case « Générer les notifications de stock bas » (P2C-GLOBAL). Déplace, <b>sans changement
/// de comportement observable</b>, la logique identique qui vivait dans
/// <c>NotificationsListViewModel.GenerateLowStockNotificationsAsync</c> et
/// <c>MainWindowViewModel.LoadUnreadNotificationsCountAsync</c>.
/// </summary>
/// <remarks>
/// Le texte des notifications (« Stock bas : … », « Le produit … est en stock bas (x/y) ») et l'anti-doublon (une
/// seule notification non lue par produit) sont préservés au caractère près. Écriture unique
/// (<c>SaveChangesAsync</c> après la boucle), <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class GenerateLowStockNotificationsUseCase : IGenerateLowStockNotificationsUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public GenerateLowStockNotificationsUseCase(
        IProductRepository productRepository,
        INotificationRepository notificationRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<GenerateLowStockNotificationsResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAllAsync(cancellationToken);
        var lowStockProducts = products.Where(p => p.StockQuantity <= p.StockAlertThreshold).ToList();

        var created = 0;
        foreach (var product in lowStockProducts)
        {
            // Anti-doublon : ne pas recréer une notification « stock bas » non lue déjà présente pour ce produit.
            var existingNotifications = await _notificationRepository.GetAllAsync(cancellationToken);
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
                    CreatedAt = DateTime.Now,
                };

                await _notificationRepository.CreateAsync(notification, cancellationToken);
                created++;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var unread = await _notificationRepository.CountUnreadAsync(cancellationToken);
        return new GenerateLowStockNotificationsResult { CreatedCount = created, UnreadCount = unread };
    }
}
