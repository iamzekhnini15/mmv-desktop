namespace MMV.Application.Abstractions;

/// <summary>
/// Marqueur neutre de l'assembly <c>MMV.Application</c>. Aucune sémantique métier.
/// </summary>
/// <remarks>
/// Sert d'ancre de type (<c>typeof(IApplicationMarker).Assembly</c>) pour un futur enregistrement
/// des use cases par scan d'assembly (P2B-2C+) et pour un éventuel test d'architecture vérifiant
/// les références de la couche Application. Squelette créé en P2B-2B ; à compléter par les
/// interfaces de use cases réelles lors des prochains slices.
/// </remarks>
public interface IApplicationMarker
{
}
