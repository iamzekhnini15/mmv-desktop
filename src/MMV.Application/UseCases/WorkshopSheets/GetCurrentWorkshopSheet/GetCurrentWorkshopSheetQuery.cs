using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.WorkshopSheets.GetCurrentWorkshopSheet;

/// <summary>
/// Entrée (DTO) de la query <see cref="GetCurrentWorkshopSheetUseCase"/> — P3-6B.
/// </summary>
public sealed class GetCurrentWorkshopSheetQuery
{
    /// <summary>Identifiant de la commande dont on lit la fiche courante.</summary>
    public long OrderId { get; init; }
}

/// <summary>
/// Sortie (DTO) de la query <see cref="GetCurrentWorkshopSheetUseCase"/> — P3-6B.
/// </summary>
/// <remarks>
/// <see cref="SheetFound"/> distingue « commande sans aucune fiche » (commande historique antérieure à P3-6B, ou
/// fabrication non commencée) d'une fiche réellement présente.
/// </remarks>
public sealed class GetCurrentWorkshopSheetResult
{
    /// <summary>Indique si la commande a été retrouvée.</summary>
    public bool OrderFound { get; init; }

    /// <summary>Indique si la commande possède une version courante de fiche.</summary>
    public bool SheetFound { get; init; }

    /// <summary>Fiche courante, ou <c>null</c>.</summary>
    public WorkshopSheetDto? Sheet { get; init; }
}

/// <summary>
/// Query « Lire la fiche atelier courante d'une commande » (P3-6B).
/// </summary>
public interface IGetCurrentWorkshopSheetUseCase
{
    /// <summary>Lit la version courante de la fiche d'une commande.</summary>
    Task<GetCurrentWorkshopSheetResult> ExecuteAsync(GetCurrentWorkshopSheetQuery query, CancellationToken cancellationToken = default);
}
