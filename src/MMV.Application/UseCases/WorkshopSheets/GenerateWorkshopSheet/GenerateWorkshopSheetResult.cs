namespace MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;

/// <summary>
/// Sortie (DTO) du use case <see cref="GenerateWorkshopSheetUseCase"/> — P3-6B.
/// </summary>
/// <remarks>
/// Contrat « introuvable » homogène au dépôt (P3-1) : commande absente ⇒ <see cref="OrderFound"/> = <c>false</c>
/// et <see cref="Sheet"/> = <c>null</c>, sans aucune écriture. Les refus <b>durs</b> (statut interdit, commande
/// sans article) sont des <c>BusinessRuleException</c>, pas des drapeaux de résultat.
/// </remarks>
public sealed class GenerateWorkshopSheetResult
{
    /// <summary>Indique si la commande a été retrouvée.</summary>
    public bool OrderFound { get; init; }

    /// <summary>Version de fiche nouvellement créée, ou <c>null</c> si la commande est introuvable.</summary>
    public WorkshopSheetDto? Sheet { get; init; }
}
