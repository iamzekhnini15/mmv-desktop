using MMV.Domain.Enums;

namespace MMV.Application.UseCases.Users.CreateUser;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreateUserUseCase"/> — « Créer un utilisateur » (P2C-GLOBAL). Porte les
/// champs saisis dans <c>UserFormViewModel</c> (mode création), mot de passe en clair inclus (haché par le use case).
/// </summary>
/// <remarks>
/// Déplacement iso-fonctionnel de la branche création de <c>UserFormViewModel.ExecuteSaveAsync</c> (vérification
/// d'unicité du nom d'utilisateur, hachage BCrypt, <c>ISupplierRepository.CreateAsync</c> +
/// <c>IUnitOfWork.SaveChangesAsync</c>).
/// </remarks>
public sealed class CreateUserCommand
{
    /// <summary>Nom d'utilisateur (unique, validé côté UI).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Prénom.</summary>
    public string FirstName { get; init; } = string.Empty;

    /// <summary>Nom.</summary>
    public string LastName { get; init; } = string.Empty;

    /// <summary>
    /// Rôle attribué. <b>Obligatoire</b> (P3-10) : <c>null</c> est refusé par une erreur de validation.
    /// </summary>
    /// <remarks>
    /// Le type était <c>UserRole</c> non nullable, dont le défaut CLR est la première valeur de l'enum —
    /// <c>Admin</c>. Une commande construite sans mention du rôle créait donc silencieusement un
    /// <b>administrateur</b> (audit P3-10 §16, R3) : une omission produisait le privilège maximal. Rendre le
    /// champ nullable rend l'omission détectable, donc refusable, au lieu d'être interprétée.
    /// </remarks>
    public UserRole? Role { get; init; }

    /// <summary>Indique si le compte est actif.</summary>
    public bool IsActive { get; init; }

    /// <summary>Mot de passe en clair (haché par le use case avant persistance).</summary>
    public string Password { get; init; } = string.Empty;
}
