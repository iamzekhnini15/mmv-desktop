using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Users.UpdateUser;

/// <summary>
/// Entrée (DTO) du use case <see cref="UpdateUserUseCase"/> — « Modifier un utilisateur » (P2C-GLOBAL). Porte
/// l'identifiant et les champs modifiés dans <c>UserFormViewModel</c> (mode édition).
/// </summary>
/// <remarks>
/// Déplacement iso-fonctionnel de la branche édition de <c>UserFormViewModel.ExecuteSaveAsync</c> : vérification
/// d'unicité si le nom d'utilisateur change, re-hachage du mot de passe uniquement s'il est renseigné.
/// </remarks>
public sealed class UpdateUserCommand
{
    /// <summary>Identifiant de l'utilisateur à modifier.</summary>
    public long UserId { get; init; }

    /// <summary>Nom d'utilisateur (unique, validé côté UI).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Prénom.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Rôle attribué. <b>Obligatoire</b> (P3-10) : <c>null</c> est refusé par une erreur de validation.
    /// Voir <c>CreateUserCommand.Role</c> — l'omission ne doit jamais valoir <c>Admin</c>.
    /// </summary>
    public UserRole? Role { get; init; }

    /// <summary>Indique si le compte est actif.</summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Nouveau mot de passe en clair. <c>null</c> ou blanc = mot de passe inchangé (comme le flux d'origine).
    /// </summary>
    public string? Password { get; init; }
}
