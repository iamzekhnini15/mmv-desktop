namespace MMV.Domain.Optics;

/// <summary>
/// Résultat d'une transposition optique (P3-6B) : la correction obtenue <b>et</b> l'information explicite de
/// savoir si une notation transposée <b>significative</b> a réellement pu être produite.
/// </summary>
/// <remarks>
/// <para>
/// Ce drapeau évite l'ambiguïté la plus dangereuse du domaine : une correction renvoyée « inchangée » peut
/// signifier <i>« rien à transposer »</i> (pas d'astigmatisme) ou <i>« transposition impossible »</i> (cylindre
/// présent mais axe manquant). Dans les deux cas <see cref="IsTransposed"/> vaut <c>false</c> et
/// <see cref="Correction"/> est la source telle quelle — <b>aucun axe n'est jamais inventé</b>.
/// </para>
/// </remarks>
/// <param name="IsTransposed">
/// <c>true</c> uniquement si une notation atelier distincte a été calculée (cylindre non nul <b>et</b> axe présent).
/// </param>
/// <param name="Correction">
/// La correction transposée si <paramref name="IsTransposed"/> est <c>true</c> ; sinon la correction source,
/// renvoyée inchangée.
/// </param>
public readonly record struct OpticalTranspositionResult(bool IsTransposed, OpticalCorrection Correction);
