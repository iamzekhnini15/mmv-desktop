namespace MMV.Application.UseCases.Users.SetUserActive;

/// <summary>
/// Entrée (DTO) du use case <see cref="SetUserActiveUseCase"/> — « Activer / désactiver un utilisateur »
/// (P2C-GLOBAL). Porte l'identifiant et l'état d'activation cible sélectionnés dans <c>UsersListViewModel</c>.
/// </summary>
/// <remarks>
/// Déplacement iso-fonctionnel de <c>UsersListViewModel.ToggleActiveAsync</c> (qui basculait <c>IsActive</c> puis
/// appelait <c>IUserRepository.UpdateAsync</c> + <c>IUnitOfWork.SaveChangesAsync</c>).
/// </remarks>
public sealed class SetUserActiveCommand
{
    /// <summary>Identifiant de l'utilisateur concerné.</summary>
    public long UserId { get; init; }

    /// <summary>État d'activation cible.</summary>
    public bool IsActive { get; init; }
}
