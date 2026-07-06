namespace MMV.Application.UseCases.Users.SetUserActive;

/// <summary>
/// Sortie (DTO) du use case <see cref="SetUserActiveUseCase"/>.
/// </summary>
public sealed class SetUserActiveResult
{
    /// <summary>Indique si l'utilisateur visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool UserFound { get; init; }

    /// <summary>Identifiant de l'utilisateur visé (écho de l'entrée).</summary>
    public long UserId { get; init; }

    /// <summary>État d'activation appliqué (écho de l'entrée si trouvé).</summary>
    public bool IsActive { get; init; }
}
