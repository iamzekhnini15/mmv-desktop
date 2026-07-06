namespace MMV.Application.UseCases.Users.UpdateUser;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateUserUseCase"/>.
/// </summary>
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
}
