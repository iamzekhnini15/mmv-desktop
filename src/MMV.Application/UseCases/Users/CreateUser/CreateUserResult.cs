using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Users.CreateUser;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateUserUseCase"/>.
/// </summary>
/// <remarks>
/// <b>P3-10.</b> Extension <b>additive</b> à la convention P3-1 (identique à <c>CreateSupplierResult</c>) :
/// <see cref="ValidationErrors"/> porte désormais les refus de saisie — mot de passe faible, rôle manquant ou
/// invalide, login déjà pris, champs de profil invalides. <see cref="UsernameTaken"/> est <b>conservé</b> pour ne
/// pas casser les appelants existants, et reste cohérent avec <see cref="ValidationErrors"/> : lorsqu'il vaut
/// <c>true</c>, la liste contient l'erreur de login correspondante.
/// </remarks>
public sealed class CreateUserResult
{
    /// <summary>
    /// Indique qu'un utilisateur portant déjà ce nom existe : aucune écriture effectuée. Reproduit la garde
    /// d'unicité du flux d'origine (message « Ce nom d'utilisateur est déjà utilisé. »). Depuis P3-10, la
    /// comparaison porte sur la forme <b>normalisée</b> du login : une variante de casse est un doublon.
    /// </summary>
    public bool UsernameTaken { get; init; }

    /// <summary>Identifiant attribué à l'utilisateur créé (0 si la commande a été refusée).</summary>
    public long UserId { get; init; }

    /// <summary>
    /// Erreurs de validation de commande (P3-1/P3-10). <b>Vide</b> = commande valide et utilisateur persisté.
    /// L'ordre est déterministe : les erreurs de politique de mot de passe puis de rôle précèdent celles du
    /// validateur Domain, elles-mêmes rendues dans l'ordre des règles.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si l'utilisateur a été persisté).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
