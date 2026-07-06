using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Users.CreateUser;

/// <summary>
/// Cas d'utilisation « Créer un utilisateur » (P2C-GLOBAL). Remplace la branche création auparavant portée par
/// <c>UserFormViewModel.ExecuteSaveAsync</c> (unicité du nom d'utilisateur, hachage du mot de passe, persistance).
/// </summary>
public interface ICreateUserUseCase
{
    /// <summary>
    /// Crée l'utilisateur décrit par <paramref name="command"/>. Si le nom d'utilisateur est déjà pris, aucune
    /// écriture n'est effectuée (<see cref="CreateUserResult.UsernameTaken"/> = <c>true</c>).
    /// </summary>
    Task<CreateUserResult> ExecuteAsync(CreateUserCommand command, CancellationToken cancellationToken = default);
}
