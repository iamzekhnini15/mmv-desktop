using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Users.SetUserActive;

/// <summary>
/// Cas d'utilisation « Activer / désactiver un utilisateur » (P2C-GLOBAL). Remplace l'orchestration auparavant
/// portée par <c>UsersListViewModel.ToggleActiveAsync</c>.
/// </summary>
public interface ISetUserActiveUseCase
{
    /// <summary>
    /// Applique l'état d'activation décrit par <paramref name="command"/>. Si l'utilisateur est introuvable,
    /// aucune écriture n'est effectuée (<see cref="SetUserActiveResult.UserFound"/> = <c>false</c>).
    /// </summary>
    Task<SetUserActiveResult> ExecuteAsync(SetUserActiveCommand command, CancellationToken cancellationToken = default);
}
