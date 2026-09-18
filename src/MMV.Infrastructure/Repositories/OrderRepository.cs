using Microsoft.EntityFrameworkCore;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;
using MMV.Infrastructure.Data;

namespace MMV.Infrastructure.Repositories;

/// <summary>
/// Implémentation du repository pour la gestion des commandes.
/// </summary>
public class OrderRepository : BaseRepository<Order, long>, IOrderRepository
{
    public OrderRepository(OpticDbContext context) : base(context) { }

    /// <summary>
    /// Récupère toutes les commandes avec leurs articles (OrderItems) et la vente parente.
    /// </summary>
    public async Task<IList<Order>> GetAllWithItemsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.Sale)
                .ThenInclude(s => s!.Customer)
            .AsNoTracking()
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère une commande avec tous ses articles et la vente parente.
    /// </summary>
    public async Task<Order?> GetWithItemsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.Sale)
                .ThenInclude(s => s!.Customer)
            .FirstOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);
    }

    /// <summary>
    /// Recherche une commande par son numéro.
    /// </summary>
    public async Task<Order?> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderNumber);
        
        return await GetQueryable()
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes d'une vente spécifique.
    /// </summary>
    public async Task<IList<Order>> GetBySaleIdAsync(long saleId, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.SaleId == saleId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Prise de statut atomique conditionnelle (P3-5) : un unique <c>UPDATE … WHERE OrderId = @id AND Status = @expected</c>
    /// via <c>ExecuteUpdateAsync</c>. La base décide en une seule instruction ; <c>rows == 1</c> ⇒ transition prise,
    /// <c>rows == 0</c> ⇒ statut stocké différent (déjà avancé, rejoué, ou commande introuvable). Contourne le change
    /// tracker : aucune entité suivie n'est réécrite, donc pas de double avancement.
    /// </summary>
    public async Task<bool> TryTransitionStatusAsync(long orderId, OrderStatus expectedStatus, OrderStatus nextStatus, CancellationToken cancellationToken = default)
    {
        var rowsAffected = await _context.Orders
            .Where(o => o.OrderId == orderId && o.Status == expectedStatus)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.Status, nextStatus),
                cancellationToken);

        return rowsAffected == 1;
    }

    /// <inheritdoc />
    public async Task<OrderReadyTransitionOutcome> TryTransitionWithWorkshopSheetAsync(
        long orderId,
        OrderStatus expectedStatus,
        OrderStatus nextStatus,
        long? expectedWorkshopSheetId,
        string? expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        // Prise ATOMIQUE liée à la fiche autoritaire : le statut ET l'exigence de fiche sont évalués par la même
        // instruction que l'écriture, sous la forme d'un EXISTS / NOT EXISTS corrélé. Aucune lecture préalable ne
        // participe à la décision : une régénération concurrente survenue depuis la vérification métier fait
        // simplement échouer la condition, et la commande reste en contrôle qualité.
        var sheets = _context.WorkshopSheets;

        var rowsAffected = await _context.Orders
            .Where(o => o.OrderId == orderId
                        && o.Status == expectedStatus
                        && (expectedWorkshopSheetId == null
                                // Compatibilité historique : autorisée seulement s'il n'existe TOUJOURS aucune
                                // fiche. Une fiche Pending créée entre-temps invalide l'autorisation.
                                ? !sheets.Any(w => w.OrderId == orderId)
                                // Sinon la fiche vérifiée doit encore être la version courante, validée, et
                                // porter l'empreinte attendue.
                                : sheets.Any(w => w.WorkshopSheetId == expectedWorkshopSheetId
                                                  && w.OrderId == orderId
                                                  && w.IsCurrent
                                                  && w.QcStatus == WorkshopSheetQcStatus.Passed
                                                  && (expectedFingerprint == null
                                                      || w.TechnicalFingerprint == expectedFingerprint))))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.Status, nextStatus),
                cancellationToken)
            .ConfigureAwait(false);

        if (rowsAffected == 1)
        {
            return OrderReadyTransitionOutcome.Taken;
        }

        // Aucune ligne affectée : la décision est déjà prise (rien n'a été écrit). La lecture ci-dessous ne sert
        // qu'à DIAGNOSTIQUER laquelle des deux conditions a cédé, afin de renvoyer le bon message métier.
        var actualStatus = await _context.Orders
            .Where(o => o.OrderId == orderId)
            .Select(o => (OrderStatus?)o.Status)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return actualStatus == expectedStatus
            ? OrderReadyTransitionOutcome.WorkshopSheetRequirementNotMet
            : OrderReadyTransitionOutcome.StatusConflict;
    }

    /// <summary>
    /// Récupère les commandes par statut.
    /// </summary>
    public async Task<IList<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.Status == status)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes créées entre deux dates.
    /// </summary>
    public async Task<IList<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.OrderDate >= startDate && o.OrderDate <= endDate)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Récupère les commandes en retard à l'instant fourni par l'appelant.
    /// </summary>
    /// <remarks>
    /// P4-5D : <c>DateTime.Now</c> est retiré d'ici. La comparaison étant traduite en SQL, un instant
    /// local y était confronté à une colonne UTC (ADR-PROD-DB-004 §2.2, décision 3b). Le convertisseur
    /// validant du modèle s'applique aussi aux <b>paramètres</b> de requête : un <paramref name="asOfUtc"/>
    /// non-UTC fait désormais lever <c>NonUtcDateTimeException</c> au lieu de filtrer de travers.
    /// </remarks>
    public async Task<IList<Order>> GetOverdueOrdersAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
    {
        return await GetQueryable()
            .Where(o => o.EstimatedDelivery.HasValue &&
                        o.EstimatedDelivery < asOfUtc &&
                        o.Status != OrderStatus.Delivered)
            .OrderBy(o => o.EstimatedDelivery)
            .ToListAsync(cancellationToken);
    }

    // =============================================================================================================
    // Fiche atelier (P3-6B)
    // =============================================================================================================

    /// <inheritdoc />
    public async Task<WorkshopSheet?> GetCurrentWorkshopSheetAsync(long orderId, bool includeItems = false, CancellationToken cancellationToken = default)
    {
        var query = _context.WorkshopSheets.Where(w => w.OrderId == orderId && w.IsCurrent);

        if (includeItems)
        {
            // Les lignes sont restituées dans l'ordre figé du bon d'atelier, jamais dans l'ordre de la base.
            query = query.Include(w => w.Items.OrderBy(i => i.Position));
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkshopSheet?> GetWorkshopSheetAsync(long workshopSheetId, CancellationToken cancellationToken = default)
    {
        return await _context.WorkshopSheets
            .FirstOrDefaultAsync(w => w.WorkshopSheetId == workshopSheetId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IList<WorkshopSheet>> GetWorkshopSheetVersionsAsync(long orderId, CancellationToken cancellationToken = default)
    {
        return await _context.WorkshopSheets
            .Where(w => w.OrderId == orderId)
            .Include(w => w.Items.OrderBy(i => i.Position))
            .AsNoTracking()
            .OrderByDescending(w => w.Version)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkshopSheet> CreateNextWorkshopSheetVersionAsync(
        Order order,
        WorkshopSheetVersionPrecondition precondition,
        DateTime createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        // 1) Le numéro suivant découle de la version que l'appelant a réellement lue — jamais d'un MAX(Version)
        //    relu ici, qui ferait silencieusement « repartir » d'une régénération concurrente jamais vue.
        var nextVersion = precondition.NextVersion;

        // 2) COMPARE-AND-SWAP : la bascule ne réussit que si la version attendue est ENCORE la version courante
        //    de cette commande. Exactement une ligne doit être affectée ; sinon un autre poste a régénéré depuis
        //    la lecture, et la demande fondée sur l'ancienne version est refusée sans rien écrire de plus.
        if (precondition.ExpectsExistingVersion)
        {
            var switched = await _context.WorkshopSheets
                .Where(w => w.WorkshopSheetId == precondition.CurrentWorkshopSheetId!.Value
                            && w.OrderId == order.OrderId
                            && w.IsCurrent)
                .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.IsCurrent, false), cancellationToken)
                .ConfigureAwait(false);

            if (switched != 1)
            {
                throw new WorkshopSheetVersionConflictException(order.OrderId, nextVersion);
            }
        }

        // 3) Construire le snapshot (Domain pur : aucune écriture sur la commande, l'ordonnance ou le catalogue).
        var sheet = WorkshopSheetFactory.Create(order, nextVersion, createdAt);

        await _context.WorkshopSheets.AddAsync(sheet, cancellationToken).ConfigureAwait(false);

        // 4) La base reste l'arbitre de dernier recours : unicité (OrderId, Version) ET unicité de la version
        //    courante — c'est elle, notamment, qui tranche deux créations concurrentes de la version 1, pour
        //    lesquelles il n'existe aucune version antérieure sur laquelle faire un compare-and-swap. Une
        //    génération concurrente perd ici proprement, sans qu'aucun message EF/SQLite ne franchisse
        //    l'Infrastructure. Aucune re-tentative : après violation de contrainte la transaction est perdue.
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            throw new WorkshopSheetVersionConflictException(order.OrderId, nextVersion);
        }

        return sheet;
    }

    /// <inheritdoc />
    public async Task<bool> TryTakeWorkshopSheetQcDecisionAsync(
        long workshopSheetId,
        WorkshopSheetQcStatus decision,
        string? comment,
        DateTime completedAt,
        CancellationToken cancellationToken = default)
    {
        // Prise ATOMIQUE de la décision : un seul UPDATE conditionnel. La condition porte à la fois sur
        // « aucune décision encore prise » (QcStatus = Pending) et sur « version toujours courante » (IsCurrent),
        // de sorte qu'une double validation, ou la validation d'une version rendue obsolète par une régénération
        // concurrente, n'affecte aucune ligne.
        var rowsAffected = await _context.WorkshopSheets
            .Where(w => w.WorkshopSheetId == workshopSheetId
                        && w.IsCurrent
                        && w.QcStatus == WorkshopSheetQcStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(w => w.QcStatus, decision)
                    .SetProperty(w => w.QcComment, comment)
                    .SetProperty(w => w.QcCompletedAt, (DateTime?)completedAt),
                cancellationToken)
            .ConfigureAwait(false);

        return rowsAffected == 1;
    }
}
