using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Users.UpdateUser;

/// <summary>
/// Cas d'utilisation « Modifier un utilisateur » (P2C-GLOBAL). Remplace la branche édition auparavant portée par
/// <c>UserFormViewModel.ExecuteSaveAsync</c>.
/// </summary>
public interface IUpdateUserUseCase
{
    /// <summary>
    /// Applique les modifications décrites par <paramref name="command"/>. Renvoie <c>UserFound = false</c> si
    /// l'utilisateur est introuvable, ou <c>UsernameTaken = true</c> si le nouveau nom est déjà pris — dans les
    /// deux cas, aucune écriture n'est effectuée.
    /// </summary>
    Task<UpdateUserResult> ExecuteAsync(UpdateUserCommand command, CancellationToken cancellationToken = default);
}
