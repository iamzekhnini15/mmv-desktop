using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Users.ListUsers;

/// <summary>
/// Implémentation du query use case « Lister les utilisateurs » (P2D-1). Déplace, <b>sans changement de
/// comportement observable</b>, la lecture qui vivait dans <c>UsersListViewModel.LoadUsersAsync</c>
/// (<c>IUserRepository.GetAllAsync</c>). Projette chaque entité vers un <see cref="UserListItemDto"/> plat :
/// aucune entité <c>User</c> suivie par EF ne franchit la frontière UI.
/// </summary>
public sealed class ListUsersUseCase : IListUsersUseCase
{
    private readonly IUserRepository _userRepository;

    public ListUsersUseCase(IUserRepository userRepository)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserListItemDto>> ExecuteAsync(ListUsersQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var users = await _userRepository.GetAllAsync(cancellationToken);

        return users.Select(u => new UserListItemDto
        {
            UserId = u.UserId,
            Username = u.Username,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Role = u.Role,
            IsActive = u.IsActive,
            LastLogin = u.LastLogin,
            CreatedAt = u.CreatedAt,
        }).ToList();
    }
}
