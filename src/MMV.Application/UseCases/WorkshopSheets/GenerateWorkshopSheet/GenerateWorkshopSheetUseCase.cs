using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;

/// <summary>
/// Implémentation du use case « Générer une version de fiche atelier » (P3-6B) : crée une <b>nouvelle version</b>
/// de la fiche d'une commande, reconstruite depuis la commande réelle, et rend l'ancienne version non courante.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reprise atelier, pas correction de commande.</b> Ce use case sert à régénérer une fiche pour la même
/// commande — typiquement après un contrôle qualité refusé, ou une réimpression consécutive à une reprise de
/// montage. Il ne rouvre <b>jamais</b> l'édition de la commande, verrouillée depuis <c>InProgress</c> par P3-6.
/// </para>
/// <para>
/// <b>Statuts autorisés.</b> <c>InProgress</c> et <c>QualityCheck</c> uniquement
/// (<see cref="WorkshopSheetPolicy.CanGenerateForStatus"/>) : avant, la fabrication n'a pas commencé (la première
/// version est créée automatiquement à l'entrée en fabrication) ; après, le travail d'atelier est terminé.
/// </para>
/// <para>
/// <b>Transaction.</b> La bascule « ancienne version non courante » et l'insertion de la nouvelle s'exécutent
/// dans un unique <see cref="ITransactionRunner"/> : un conflit de génération concurrent
/// (<see cref="WorkshopSheetVersionConflictException"/>) annule <b>tout</b>, de sorte que l'ancienne version
/// reste courante et qu'aucune version partielle ne subsiste.
/// </para>
/// <para>
/// <b>QC non reporté.</b> La nouvelle version repart <c>Pending</c> : le résultat de contrôle d'une version
/// précédente n'est jamais recopié — ce serait valider une fabrication qui n'a pas été contrôlée.
/// </para>
/// </remarks>
public sealed class GenerateWorkshopSheetUseCase : IGenerateWorkshopSheetUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ITransactionRunner _transactionRunner;

    public GenerateWorkshopSheetUseCase(IOrderRepository orderRepository, ITransactionRunner transactionRunner)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        // Frontière transactionnelle obligatoire : bascule de version courante + insertion = tout ou rien.
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<GenerateWorkshopSheetResult> ExecuteAsync(GenerateWorkshopSheetCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        return await _transactionRunner.RunAsync(async token =>
        {
            // Charger la commande AVEC ses lignes et leurs produits : le snapshot doit être construit sur les
            // données réelles, jamais sur OrderDetailsDto (qui perd usage, prisme, base et acuité).
            var order = await _orderRepository.GetWithItemsAsync(command.OrderId, token).ConfigureAwait(false);
            if (order is null)
            {
                return new GenerateWorkshopSheetResult { OrderFound = false };
            }

            if (!WorkshopSheetPolicy.CanGenerateForStatus(order.Status))
            {
                throw new BusinessRuleException(WorkshopSheetPolicy.GenerationStatusMessage);
            }

            if (order.OrderItems is null || !order.OrderItems.Any())
            {
                throw new BusinessRuleException(WorkshopSheetPolicy.GenerationWithoutItemsMessage);
            }

            // Version courante lue ⇒ transmise comme PRÉCONDITION de concurrence : la nouvelle version ne sera
            // créée que si celle-ci est encore la version courante à l'instant de la bascule. Deux demandes
            // simultanées parties de la même version produisent ainsi une seule version suivante, jamais deux
            // versions successives dont la seconde ignorerait la première.
            var current = await _orderRepository
                .GetCurrentWorkshopSheetAsync(order.OrderId, includeItems: false, token)
                .ConfigureAwait(false);

            var precondition = current is null
                ? WorkshopSheetVersionPrecondition.None
                : WorkshopSheetVersionPrecondition.From(current);

            var sheet = await _orderRepository
                .CreateNextWorkshopSheetVersionAsync(order, precondition, DateTime.UtcNow, token)
                .ConfigureAwait(false);

            // La version vient d'être construite depuis cette commande : elle est à jour par construction.
            return new GenerateWorkshopSheetResult
            {
                OrderFound = true,
                Sheet = WorkshopSheetOperations.ToDto(sheet, isUpToDate: true)
            };
        }, cancellationToken).ConfigureAwait(false);
    }
}
