namespace MMV.Application.UseCases.Users.CreateUser;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateUserUseCase"/>.
/// </summary>
public sealed class CreateUserResult
{
    /// <summary>
    /// Indique qu'un utilisateur portant déjà ce nom existe : aucune écriture effectuée. Reproduit la garde
    /// d'unicité du flux d'origine (message « Ce nom d'utilisateur est déjà utilisé. »).
    /// </summary>
    public bool UsernameTaken { get; init; }

    /// <summary>Identifiant attribué à l'utilisateur créé (0 si <see cref="UsernameTaken"/>).</summary>
    public long UserId { get; init; }
}
