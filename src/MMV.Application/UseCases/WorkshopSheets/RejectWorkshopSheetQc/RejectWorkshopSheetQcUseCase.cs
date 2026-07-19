using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.WorkshopSheets.RejectWorkshopSheetQc;

/// <summary>
/// Entrée (DTO) du use case <see cref="RejectWorkshopSheetQcUseCase"/> — « Refuser le contrôle qualité
/// atelier » (P3-6B).
/// </summary>
public sealed class RejectWorkshopSheetQcCommand
{
    /// <summary>Identifiant de la version de fiche contrôlée (celle affichée au contrôleur).</summary>
    public long WorkshopSheetId { get; init; }

    /// <summary>Motif du refus — <b>obligatoire</b> : un refus non motivé n'est pas exploitable en atelier.</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Sortie (DTO) du use case <see cref="RejectWorkshopSheetQcUseCase"/> — P3-6B.
/// </summary>
public sealed class RejectWorkshopSheetQcResult
{
    /// <summary>Indique si la version de fiche a été retrouvée.</summary>
    public bool SheetFound { get; init; }

    /// <summary>Fiche après refus, ou <c>null</c> si introuvable.</summary>
    public WorkshopSheetDto? Sheet { get; init; }
}

/// <summary>
/// Use case « Refuser le contrôle qualité atelier » (P3-6B).
/// </summary>
public interface IRejectWorkshopSheetQcUseCase
{
    /// <summary>Refuse le contrôle qualité de la version de fiche visée (commentaire obligatoire).</summary>
    Task<RejectWorkshopSheetQcResult> ExecuteAsync(RejectWorkshopSheetQcCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implémentation du use case « Refuser le contrôle qualité atelier » (P3-6B) : fait passer la version courante
/// de <c>Pending</c> à <c>Failed</c>, de façon <b>atomique</b> et <b>définitive</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Un refus ne se corrige pas, il se reprend.</b> La version refusée reste <b>historique et immuable</b> :
/// elle témoigne du défaut constaté. La reprise d'atelier passe par une <b>nouvelle version</b>
/// (<c>GenerateWorkshopSheetUseCase</c>), qui repart <c>Pending</c> — jamais par une réouverture de la décision.
/// La commande, elle, n'est pas rouverte à l'édition (verrouillée depuis <c>InProgress</c> par P3-6).
/// </para>
/// </remarks>
public sealed class RejectWorkshopSheetQcUseCase : IRejectWorkshopSheetQcUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ITransactionRunner _transactionRunner;

    public RejectWorkshopSheetQcUseCase(IOrderRepository orderRepository, ITransactionRunner transactionRunner)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<RejectWorkshopSheetQcResult> ExecuteAsync(RejectWorkshopSheetQcCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        return await _transactionRunner.RunAsync(async token =>
        {
            var sheet = await WorkshopSheetQcDecision
                .ApplyAsync(_orderRepository, command.WorkshopSheetId, WorkshopSheetQcStatus.Failed, command.Comment, token)
                .ConfigureAwait(false);

            return sheet is null
                ? new RejectWorkshopSheetQcResult { SheetFound = false }
                : new RejectWorkshopSheetQcResult { SheetFound = true, Sheet = sheet };
        }, cancellationToken).ConfigureAwait(false);
    }
}
