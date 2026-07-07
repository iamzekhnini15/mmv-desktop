namespace MMV.Application.Common;

/// <summary>
/// Erreur de <b>validation de commande</b> (précondition d'un use case d'écriture) exposée par la couche
/// Application, <b>sans</b> dépendance FluentValidation dans le contrat public. Introduite en <b>P3-1</b> comme
/// socle transverse minimal : une commande invalide n'est plus silencieusement persistée ; le use case renvoie
/// la liste des erreurs sur son <c>*Result</c> et n'écrit rien.
/// </summary>
/// <remarks>
/// Type <b>neutre</b> (aucune dépendance EF / Avalonia / MVVM / FluentValidation). Les use cases produisent ces
/// erreurs en réutilisant les validateurs FluentValidation <b>déjà présents dans le Domain</b> (ex.
/// <c>CustomerValidator</c>) via <see cref="CommandValidation"/>, puis en mappant chaque échec vers ce DTO. La
/// couche UI peut ainsi afficher les messages sans connaître FluentValidation.
/// </remarks>
/// <param name="PropertyName">Nom de la propriété fautive (ex. <c>FirstName</c>, <c>Email</c>).</param>
/// <param name="Message">Message d'erreur métier normalisé (déjà localisé côté validateur Domain).</param>
public sealed record ValidationError(string PropertyName, string Message);
