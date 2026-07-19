namespace MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;

/// <summary>
/// Entrée (DTO) du use case <see cref="GenerateWorkshopSheetUseCase"/> — « Générer une version de fiche
/// atelier » (P3-6B).
/// </summary>
public sealed class GenerateWorkshopSheetCommand
{
    /// <summary>Identifiant de la commande dont une nouvelle version de fiche doit être générée.</summary>
    public long OrderId { get; init; }
}
