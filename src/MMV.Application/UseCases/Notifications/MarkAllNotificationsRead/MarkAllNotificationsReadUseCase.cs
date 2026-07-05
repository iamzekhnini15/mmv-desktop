using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Notifications.MarkAllNotificationsRead;

/// <summary>
/// Implémentation du use case « Marquer toutes les notifications comme lues » (P2C-GLOBAL). Déplace, <b>sans
/// changement de comportement observable</b>, l'écriture qui vivait dans
/// <c>NotificationsListViewModel.MarkAllAsReadAsync</c>.
/// </summary>
/// <remarks>Mono-écriture (MarkAllAsRead + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.</remarks>
public sealed class MarkAllNotificationsReadUseCase : IMarkAllNotificationsReadUseCase
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public MarkAllNotificationsReadUseCase(INotificationRepository notificationRepository, IUnitOfWork unitOfWork)
    {
        _notificationRepository = notificationRepository ?? throw new ArgumentNullException(nameof(notificationRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<MarkAllNotificationsReadResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await _notificationRepository.MarkAllAsReadAsync(cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return new MarkAllNotificationsReadResult();
    }
}
