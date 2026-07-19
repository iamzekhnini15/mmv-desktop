using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Enums;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.WorkshopSheets.ValidateWorkshopSheetQc;

/// <summary>
/// Entrée (DTO) du use case <see cref="ValidateWorkshopSheetQcUseCase"/> — « Valider le contrôle qualité
/// atelier » (P3-6B).
/// </summary>
public sealed class ValidateWorkshopSheetQcCommand
{
    /// <summary>Identifiant de la version de fiche contrôlée (celle affichée au contrôleur).</summary>
    public long WorkshopSheetId { get; init; }

    /// <summary>Commentaire de contrôle, <b>facultatif</b> pour une validation.</summary>
    public string? Comment { get; init; }
}

/// <summary>
/// Sortie (DTO) du use case <see cref="ValidateWorkshopSheetQcUseCase"/> — P3-6B.
/// </summary>
public sealed class ValidateWorkshopSheetQcResult
{
    /// <summary>Indique si la version de fiche a été retrouvée.</summary>
    public bool SheetFound { get; init; }

    /// <summary>Fiche après validation, ou <c>null</c> si introuvable.</summary>
    public WorkshopSheetDto? Sheet { get; init; }
}

/// <summary>
/// Use case « Valider le contrôle qualité atelier » (P3-6B).
/// </summary>
public interface IValidateWorkshopSheetQcUseCase
{
    /// <summary>Valide le contrôle qualité de la version de fiche visée.</summary>
    Task<ValidateWorkshopSheetQcResult> ExecuteAsync(ValidateWorkshopSheetQcCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implémentation du use case « Valider le contrôle qualité atelier » (P3-6B) : fait passer la version courante
/// de <c>Pending</c> à <c>Passed</c>, de façon <b>atomique</b> et <b>définitive</b>.
/// </summary>
/// <remarks>
/// <para>
/// C'est cette décision — et elle seule — qui débloquera ensuite la transition
/// <c>QualityCheck → Ready</c> de la commande (garde posée dans <c>AdvanceOrderStatusUseCase</c>).
/// </para>
/// <para>
/// <b>Aucun auteur enregistré en V1.</b> L'identité de l'utilisateur courant n'est pas disponible proprement
/// côté Application (elle vit dans <c>ISessionService</c>, en UI) ; enregistrer un auteur exigerait soit de
/// faire dépendre l'Application de l'UI, soit d'accepter une valeur non fiable. Le champ est donc
/// <b>volontairement absent</b> plutôt que faux — report explicite.
/// </para>
/// </remarks>
public sealed class ValidateWorkshopSheetQcUseCase : IValidateWorkshopSheetQcUseCase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ITransactionRunner _transactionRunner;

    public ValidateWorkshopSheetQcUseCase(IOrderRepository orderRepository, ITransactionRunner transactionRunner)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<ValidateWorkshopSheetQcResult> ExecuteAsync(ValidateWorkshopSheetQcCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        return await _transactionRunner.RunAsync(async token =>
        {
            var sheet = await WorkshopSheetQcDecision
                .ApplyAsync(_orderRepository, command.WorkshopSheetId, WorkshopSheetQcStatus.Passed, command.Comment, token)
                .ConfigureAwait(false);

            return sheet is null
                ? new ValidateWorkshopSheetQcResult { SheetFound = false }
                : new ValidateWorkshopSheetQcResult { SheetFound = true, Sheet = sheet };
        }, cancellationToken).ConfigureAwait(false);
    }
}
