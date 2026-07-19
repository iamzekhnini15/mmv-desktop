using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.WorkshopSheets.ListWorkshopSheetVersions;

/// <summary>
/// Entrée (DTO) de la query <see cref="ListWorkshopSheetVersionsUseCase"/> — P3-6B.
/// </summary>
public sealed class ListWorkshopSheetVersionsQuery
{
    /// <summary>Identifiant de la commande dont on liste l'historique de fiches.</summary>
    public long OrderId { get; init; }
}

/// <summary>
/// Sortie (DTO) de la query <see cref="ListWorkshopSheetVersionsUseCase"/> — P3-6B.
/// </summary>
public sealed class ListWorkshopSheetVersionsResult
{
    /// <summary>Indique si la commande a été retrouvée.</summary>
    public bool OrderFound { get; init; }

    /// <summary>
    /// Historique des versions, <b>version décroissante</b> (la plus récente d'abord) — convention retenue :
    /// l'écran d'atelier s'intéresse d'abord à la fiche en cours, l'historique se déroulant en dessous.
    /// </summary>
    public IReadOnlyList<WorkshopSheetDto> Versions { get; init; } = Array.Empty<WorkshopSheetDto>();
}

/// <summary>
/// Query « Lister les versions de fiche atelier d'une commande » (P3-6B).
/// </summary>
public interface IListWorkshopSheetVersionsUseCase
{
    /// <summary>Liste l'historique des versions de fiche d'une commande.</summary>
    Task<ListWorkshopSheetVersionsResult> ExecuteAsync(ListWorkshopSheetVersionsQuery query, CancellationToken cancellationToken = default);
}
