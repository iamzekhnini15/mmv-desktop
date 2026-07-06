using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Users.SetUserActive;

/// <summary>
/// Implémentation du use case « Activer / désactiver un utilisateur » (P2C-GLOBAL). Déplace, <b>sans changement de
/// comportement observable</b>, l'écriture qui vivait dans <c>UsersListViewModel.ToggleActiveAsync</c>.
/// </summary>
/// <remarks>
/// Charge l'utilisateur par son identifiant, applique l'état d'activation cible (valeur absolue, équivalente au
/// basculement d'origine), puis persiste. Mono-écriture (Update + <c>SaveChangesAsync</c> unique),
/// <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class SetUserActiveUseCase : ISetUserActiveUseCase
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetUserActiveUseCase(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<SetUserActiveResult> ExecuteAsync(SetUserActiveCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var user = await _userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return new SetUserActiveResult { UserFound = false, UserId = command.UserId };

        user.IsActive = command.IsActive;

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetUserActiveResult { UserFound = true, UserId = command.UserId, IsActive = command.IsActive };
    }
}
