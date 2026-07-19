using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.WorkshopSheets;

/// <summary>
/// Opérations <b>partagées</b> de la fiche atelier (P3-6B) : garantir l'existence d'une version courante, et
/// projeter une fiche vers son DTO de lecture.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi un point commun.</b> Deux appelants créent des fiches : <c>GenerateWorkshopSheetUseCase</c>
/// (génération explicite d'une reprise) et <c>AdvanceOrderStatusUseCase</c> (création automatique de la
/// première version à l'entrée en fabrication). Sans ce point commun, la règle « une seule version courante,
/// snapshot complet, QC repartant à zéro » existerait en deux exemplaires — exactement la seconde source de
/// vérité que P3-6 vient d'éliminer sur le workflow.
/// </para>
/// <para>
/// <b>Sans état et sans transaction propre.</b> Ces méthodes s'exécutent <b>dans</b> la transaction de leur
/// appelant : elles n'en ouvrent aucune et n'en valident aucune. C'est ce qui permet à la génération
/// automatique d'être annulée avec la transition de statut si quoi que ce soit échoue.
/// </para>
/// </remarks>
internal static class WorkshopSheetOperations
{
    /// <summary>
    /// Garantit qu'une version courante de fiche existe pour <paramref name="order"/> : si aucune n'existe, crée
    /// la version 1 ; sinon renvoie la version existante <b>sans créer de doublon</b>.
    /// </summary>
    /// <remarks>
    /// Idempotent par nature : rejouer l'opération sur une commande qui possède déjà une fiche n'écrit rien.
    /// C'est ce qui rend sûre son insertion dans un flux de transition de statut susceptible d'être rejoué.
    /// </remarks>
    /// <returns>La version courante (existante ou nouvellement créée), et si elle vient d'être créée.</returns>
    internal static async Task<(WorkshopSheet Sheet, bool WasCreated)> EnsureCurrentSheetAsync(
        IOrderRepository orderRepository,
        Order order,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        var existing = await orderRepository
            .GetCurrentWorkshopSheetAsync(order.OrderId, includeItems: false, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return (existing, false);
        }

        // Aucune version courante lue ⇒ création de la version 1. La précondition « aucune version » n'est pas
        // vérifiable par compare-and-swap (il n'y a rien à basculer) : c'est l'unicité (OrderId, Version) qui
        // arbitre deux créations concurrentes de v1, en conflit contrôlé plutôt qu'en doublon.
        var created = await orderRepository
            .CreateNextWorkshopSheetVersionAsync(order, WorkshopSheetVersionPrecondition.None, createdAt, cancellationToken)
            .ConfigureAwait(false);

        return (created, true);
    }

    /// <summary>
    /// Indique si <paramref name="sheet"/> reflète encore les données techniques réelles de
    /// <paramref name="order"/> (empreinte identique). <c>false</c> ⇒ fiche <b>obsolète</b>.
    /// </summary>
    internal static bool IsUpToDate(WorkshopSheet sheet, Order order)
        => string.Equals(sheet.TechnicalFingerprint, WorkshopSheetFingerprint.Compute(order), StringComparison.Ordinal);

    /// <summary>
    /// Projette une fiche vers son DTO de lecture. <paramref name="isUpToDate"/> est calculé par l'appelant, qui
    /// seul dispose de la commande réelle.
    /// </summary>
    internal static WorkshopSheetDto ToDto(WorkshopSheet sheet, bool isUpToDate)
    {
        return new WorkshopSheetDto
        {
            WorkshopSheetId = sheet.WorkshopSheetId,
            OrderId = sheet.OrderId,
            Version = sheet.Version,
            IsCurrent = sheet.IsCurrent,
            CreatedAt = sheet.CreatedAt,
            OrderNumber = sheet.OrderNumberSnapshot,
            OrderDate = sheet.OrderDateSnapshot,
            EstimatedDelivery = sheet.EstimatedDeliverySnapshot,
            CustomerName = sheet.CustomerNameSnapshot,
            Instructions = sheet.InstructionsSnapshot,
            QcStatus = sheet.QcStatus,
            QcComment = sheet.QcComment,
            QcCompletedAt = sheet.QcCompletedAt,
            IsUpToDate = isUpToDate,
            Items = ProjectItems(sheet.Items)
        };
    }

    private static IReadOnlyList<WorkshopSheetItemDto> ProjectItems(IEnumerable<WorkshopSheetItem>? items)
    {
        if (items is null)
        {
            return Array.Empty<WorkshopSheetItemDto>();
        }

        // Ordre figé du bon d'atelier : jamais l'ordre de restitution de la base.
        return items
            .OrderBy(i => i.Position)
            .Select(i => new WorkshopSheetItemDto
            {
                Position = i.Position,
                ItemType = i.ItemType,
                ProductReference = i.ProductReferenceSnapshot,
                ProductName = i.ProductNameSnapshot,
                ProductCategory = i.ProductCategorySnapshot,
                Quantity = i.Quantity,
                UsageType = i.UsageType,
                SourceSphere = i.SourceSphere,
                SourceCylinder = i.SourceCylinder,
                SourceAxis = i.SourceAxis,
                Addition = i.Addition,
                PrismValue = i.PrismValue,
                PrismBase = i.PrismBase,
                VisualAcuity = i.VisualAcuity,
                TransposedSphere = i.TransposedSphere,
                TransposedCylinder = i.TransposedCylinder,
                TransposedAxis = i.TransposedAxis,
                HasTransposition = i.HasTransposition
            })
            .ToList();
    }
}
