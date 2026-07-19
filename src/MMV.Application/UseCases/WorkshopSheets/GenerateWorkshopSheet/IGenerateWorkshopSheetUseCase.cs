using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.WorkshopSheets.GenerateWorkshopSheet;

/// <summary>
/// Use case « Générer une version de fiche atelier » (P3-6B).
/// </summary>
public interface IGenerateWorkshopSheetUseCase
{
    /// <summary>Génère une nouvelle version de fiche pour la commande visée.</summary>
    Task<GenerateWorkshopSheetResult> ExecuteAsync(GenerateWorkshopSheetCommand command, CancellationToken cancellationToken = default);
}
