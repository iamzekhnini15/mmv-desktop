using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Services;

namespace MMV.Application.UseCases.Users.UpdateUser;

/// <summary>
/// Implémentation du use case « Modifier un utilisateur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la branche édition de <c>UserFormViewModel.ExecuteSaveAsync</c> vers la couche Application.
/// </summary>
/// <remarks>
/// Reproduit fidèlement l'ordre des gardes du flux d'origine : utilisateur introuvable → aucune écriture ;
/// changement de nom d'utilisateur vers un nom déjà pris → aucune écriture ; re-hachage du mot de passe seulement
/// s'il est renseigné. Mono-écriture (Update + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class UpdateUserUseCase : IUpdateUserUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthenticationService _authenticationService;

    public UpdateUserUseCase(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IAuthenticationService authenticationService)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }

    /// <inheritdoc />
    public async Task<UpdateUserResult> ExecuteAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return new UpdateUserResult { UserFound = false, UserId = command.UserId };

        var username = command.Username.Trim();

        // Vérifier l'unicité du nom d'utilisateur uniquement s'il a changé (garde du flux d'origine).
        if (user.Username != username)
        {
            var existing = await _userRepository.GetByUsernameAsync(username, cancellationToken);
            if (existing != null)
                return new UpdateUserResult { UserFound = true, UsernameTaken = true, UserId = command.UserId };
        }

        user.Username = username;
        user.FirstName = command.FirstName.Trim();
        user.LastName = command.LastName.Trim();
        user.Role = command.Role;
        user.IsActive = command.IsActive;

        if (!string.IsNullOrWhiteSpace(command.Password))
            user.PasswordHash = _authenticationService.HashPassword(command.Password);

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateUserResult { UserFound = true, UserId = command.UserId };
    }
}
