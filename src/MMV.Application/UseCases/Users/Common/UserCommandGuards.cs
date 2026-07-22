using System.Collections.Generic;
using MMV.Application.Common;
using MMV.Domain.Entities;
using MMV.Domain.Enums;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Users.Common;

/// <summary>
/// Gardes de commande <b>partagées</b> par <c>CreateUserUseCase</c> et <c>UpdateUserUseCase</c> (P3-10) :
/// politique de mot de passe, rôle explicite, message stable de login déjà pris. Propriétaire unique de ces
/// trois décisions côté Application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pourquoi un propriétaire commun.</b> Ces gardes doivent produire <b>exactement</b> le même refus sur les
/// deux chemins d'écriture. Les dupliquer dans chaque use case autoriserait la création à durcir (ou relâcher)
/// une règle que la modification laisserait en l'état — c'est précisément ainsi que la politique de mot de passe
/// avait fini appliquée par la seule UI (audit P3-10 §8, R1/R7).
/// </para>
/// <para>
/// <b>La politique reste propriétaire du Domain.</b> Aucune règle n'est réécrite ici : la validation appelle
/// <see cref="UserValidator.ValidatePasswordPolicy"/> et n'en fait que la projection vers le type neutre
/// <see cref="ValidationError"/> de la convention P3-1. Aucune seconde politique, aucune regex parallèle.
/// </para>
/// </remarks>
internal static class UserCommandGuards
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'un autre compte porte déjà cet identifiant de connexion (comparaison
    /// sur la forme normalisée). Exposé en constante pour que l'UI et les tests s'y réfèrent sans le dupliquer,
    /// et pour que la garde pré-écriture et le filet concurrent renvoient le <b>même</b> texte.
    /// </summary>
    internal const string UsernameTakenMessage = "Ce nom d'utilisateur est déjà utilisé.";

    /// <summary>
    /// Message métier stable renvoyé lorsqu'aucun rôle n'est fourni. Distinct de « rôle invalide » (produit par
    /// <see cref="UserValidator"/> sur une valeur hors enum) : une omission et une valeur inconnue sont deux
    /// erreurs de saisie différentes, et les confondre masquerait laquelle corriger.
    /// </summary>
    internal const string RoleRequiredMessage = "Le rôle est obligatoire.";

    /// <summary>
    /// Applique la politique de mot de passe du Domain et projette son refus éventuel en erreur de validation.
    /// </summary>
    /// <returns><c>null</c> si le mot de passe respecte la politique.</returns>
    /// <remarks>
    /// <see cref="UserValidator.ValidatePasswordPolicy"/> s'arrête à la <b>première</b> exigence non satisfaite et
    /// renvoie un message unique : l'ordre des refus est donc déjà déterministe et n'est ni concaténé ni agrégé
    /// ici. P3-10 <b>applique</b> la politique existante — il ne la réécrit pas pour rendre plusieurs erreurs à la
    /// fois, ce qui en changerait le comportement observable au-delà du périmètre.
    /// </remarks>
    internal static ValidationError? ValidatePassword(string? password)
    {
        var (isValid, errorMessage) = UserValidator.ValidatePasswordPolicy(password);
        return isValid ? null : new ValidationError(nameof(User.PasswordHash), errorMessage);
    }

    /// <summary>
    /// Vérifie qu'un rôle a été explicitement fourni.
    /// </summary>
    /// <returns><c>null</c> si un rôle est présent (sa validité est ensuite arbitrée par <see cref="UserValidator"/>).</returns>
    internal static ValidationError? ValidateRolePresent(UserRole? role)
        => role.HasValue ? null : new ValidationError(nameof(User.Role), RoleRequiredMessage);

    /// <summary>Erreur de validation du login déjà pris, sous sa forme stable et unique.</summary>
    internal static IReadOnlyList<ValidationError> UsernameTakenErrors()
        => new[] { new ValidationError(nameof(User.Username), UsernameTakenMessage) };
}
