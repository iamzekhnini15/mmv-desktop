using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Users.ListUsers;

/// <summary>
/// Query use case « Lister les utilisateurs » (P2D-1). Remplace la lecture directe
/// <c>IUserRepository.GetAllAsync</c> auparavant faite par <c>UsersListViewModel</c>.
/// </summary>
public interface IListUsersUseCase
{
    /// <summary>
    /// Renvoie tous les utilisateurs, projetés en <see cref="UserListItemDto"/> (jamais des entités EF suivies).
    /// La liste peut être vide mais n'est jamais <c>null</c>.
    /// </summary>
    Task<IReadOnlyList<UserListItemDto>> ExecuteAsync(ListUsersQuery query, CancellationToken cancellationToken = default);
}
