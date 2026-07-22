using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Users.UpdateUser;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateUserUseCase"/>.
/// </summary>
/// <remarks>
/// <b>P3-10.</b> Extension <b>additive</b> à la convention P3-1 : <see cref="ValidationErrors"/> porte les refus
/// de saisie (mot de passe faible, rôle manquant ou invalide, login déjà pris, profil invalide) ;
/// <see cref="UserFound"/> et <see cref="UsernameTaken"/> sont conservés tels quels.
/// </remarks>
public sealed class UpdateUserResult
{
    /// <summary>Indique si l'utilisateur visé existait. <c>false</c> = aucune écriture (message « Utilisateur introuvable. »).</summary>
    public bool UserFound { get; init; }

    /// <summary>
    /// Indique qu'un autre utilisateur porte déjà le nouveau nom d'utilisateur : aucune écriture effectuée
    /// (message « Ce nom d'utilisateur est déjà utilisé. »).
    /// </summary>
    public bool UsernameTaken { get; init; }

    /// <summary>Identifiant de l'utilisateur visé (écho de l'entrée).</summary>
    public long UserId { get; init; }

    /// <summary>
    /// Erreurs de validation de commande (P3-1/P3-10). <b>Vide</b> = commande valide et modification persistée.
    /// Ordre déterministe (politique de mot de passe et rôle avant le validateur Domain).
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si la modification a été persistée).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
