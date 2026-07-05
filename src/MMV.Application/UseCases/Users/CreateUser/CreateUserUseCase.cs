using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.Users.CreateUser;

/// <summary>
/// Implémentation du use case « Créer un utilisateur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la branche création de <c>UserFormViewModel.ExecuteSaveAsync</c> vers la couche Application.
/// Réutilise telles quelles <see cref="IUserRepository"/>, <see cref="IUnitOfWork"/> et
/// <see cref="IAuthenticationService"/> (hachage BCrypt).
/// </summary>
/// <remarks>
/// Reproduit la garde d'unicité du nom d'utilisateur (aucune écriture si déjà pris) et le hachage du mot de passe.
/// Mono-écriture (Create + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class CreateUserUseCase : ICreateUserUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthenticationService _authenticationService;

    public CreateUserUseCase(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IAuthenticationService authenticationService)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }

    /// <inheritdoc />
    public async Task<CreateUserResult> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var username = command.Username.Trim();

        var existing = await _userRepository.GetByUsernameAsync(username, cancellationToken);
        if (existing != null)
            return new CreateUserResult { UsernameTaken = true };

        var user = new User
        {
            Username = username,
            FirstName = command.FirstName.Trim(),
            LastName = command.LastName.Trim(),
            Role = command.Role,
            IsActive = command.IsActive,
            PasswordHash = _authenticationService.HashPassword(command.Password),
            CreatedAt = DateTime.UtcNow,
        };

        await _userRepository.CreateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateUserResult { UserId = user.UserId };
    }
}
