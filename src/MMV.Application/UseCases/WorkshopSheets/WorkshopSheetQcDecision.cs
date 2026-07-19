using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Enums;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.WorkshopSheets;

/// <summary>
/// Prise de décision de contrôle qualité (P3-6B), partagée par les use cases de <b>validation</b> et de
/// <b>refus</b> : mêmes préconditions, même garde atomique, seule la décision et l'exigence de commentaire
/// diffèrent.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deux gardes complémentaires</b>, comme pour le workflow de commande en P3-6 :
/// </para>
/// <list type="number">
///   <item>Les préconditions <i>métier</i> (version courante, fiche à jour, décision non encore prise) sont
///         vérifiées d'abord pour produire un <b>message métier précis</b>.</item>
///   <item>La prise <i>atomique</i> conditionnelle en base tranche ensuite le cas concurrent : deux postes
///         décidant simultanément, ou une version rendue non courante entre la lecture et l'écriture, ne
///         produisent qu'une seule décision. Aucune vérification en mémoire ne remplace cette garde.</item>
/// </list>
/// </remarks>
internal static class WorkshopSheetQcDecision
{
    /// <summary>
    /// Applique <paramref name="decision"/> à la fiche <paramref name="workshopSheetId"/> après vérification de
    /// toutes les préconditions, dans la transaction de l'appelant.
    /// </summary>
    /// <returns>La fiche mise à jour, ou <c>null</c> si elle est introuvable.</returns>
    internal static async Task<WorkshopSheetDto?> ApplyAsync(
        IOrderRepository orderRepository,
        long workshopSheetId,
        WorkshopSheetQcStatus decision,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (!WorkshopSheetPolicy.IsTerminalQcDecision(decision))
        {
            // Erreur de programmation, pas un refus métier : on ne « décide » jamais de repasser en attente.
            throw new ArgumentOutOfRangeException(
                nameof(decision), decision, "Seules les décisions Passed et Failed sont recevables.");
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (WorkshopSheetPolicy.RequiresComment(decision) && normalizedComment is null)
        {
            throw new BusinessRuleException(WorkshopSheetPolicy.QcRejectionRequiresCommentMessage);
        }

        var sheet = await orderRepository.GetWorkshopSheetAsync(workshopSheetId, cancellationToken).ConfigureAwait(false);
        if (sheet is null)
        {
            return null;
        }

        if (!sheet.IsCurrent)
        {
            throw new BusinessRuleException(WorkshopSheetPolicy.QcNotCurrentVersionMessage);
        }

        if (!WorkshopSheetPolicy.CanTakeQcDecision(sheet.QcStatus))
        {
            throw new BusinessRuleException(WorkshopSheetPolicy.QcAlreadyDecidedMessage);
        }

        // Fiche obsolète : la commande a changé depuis la génération. Valider ici reviendrait à certifier une
        // fabrication qui ne correspond plus à ce qui a été contrôlé.
        var order = await orderRepository.GetWithItemsAsync(sheet.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null || !WorkshopSheetOperations.IsUpToDate(sheet, order))
        {
            throw new BusinessRuleException(WorkshopSheetPolicy.QcObsoleteSheetMessage);
        }

        var completedAt = DateTime.UtcNow;

        var taken = await orderRepository
            .TryTakeWorkshopSheetQcDecisionAsync(workshopSheetId, decision, normalizedComment, completedAt, cancellationToken)
            .ConfigureAwait(false);

        if (!taken)
        {
            // Course perdue : un autre poste a décidé, ou a régénéré une version rendant celle-ci non courante,
            // entre les vérifications ci-dessus et cette écriture. Refus explicite, aucune écriture conservée.
            throw new BusinessRuleException(WorkshopSheetPolicy.QcAlreadyDecidedMessage);
        }

        // Refléter la décision effectivement prise en base sur l'instance renvoyée (post-écriture).
        sheet.QcStatus = decision;
        sheet.QcComment = normalizedComment;
        sheet.QcCompletedAt = completedAt;

        return WorkshopSheetOperations.ToDto(sheet, isUpToDate: true);
    }
}
