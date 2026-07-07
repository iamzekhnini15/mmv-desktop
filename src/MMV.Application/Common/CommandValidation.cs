using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;

namespace MMV.Application.Common;

/// <summary>
/// Point d'entrée unique et minimal de la <b>validation de commande</b> côté Application (socle P3-1). Exécute un
/// validateur <b>FluentValidation existant du Domain</b> (ex. <c>CustomerValidator</c>) et projette son résultat
/// vers une liste de <see cref="ValidationError"/> neutres, de sorte que :
/// <list type="bullet">
///   <item>la règle métier reste <b>unique et propriétaire du Domain</b> (aucune duplication ; cf. ADR frontières §9) ;</item>
///   <item>le contrat public des use cases ne dépend <b>pas</b> des types FluentValidation ;</item>
///   <item>aucun framework de validation « maison » n'est réintroduit.</item>
/// </list>
/// </summary>
/// <remarks>
/// FluentValidation est <b>déjà</b> une dépendance du Domain (11.9.0, licence Apache-2.0) et transite vers
/// Application ; <b>aucun</b> paquet NuGet de niveau supérieur n'est ajouté à <c>MMV.Application</c> (cf. rapport P3-1 §6).
/// </remarks>
public static class CommandValidation
{
    /// <summary>
    /// Valide <paramref name="instance"/> avec <paramref name="validator"/> et renvoie les erreurs sous forme
    /// neutre. Liste <b>vide</b> = commande valide.
    /// </summary>
    public static IReadOnlyList<ValidationError> Validate<T>(IValidator<T> validator, T instance)
    {
        if (validator is null) throw new ArgumentNullException(nameof(validator));

        var result = validator.Validate(instance);
        if (result.IsValid)
            return Array.Empty<ValidationError>();

        return result.Errors
            .Select(failure => new ValidationError(failure.PropertyName, failure.ErrorMessage))
            .ToArray();
    }
}
