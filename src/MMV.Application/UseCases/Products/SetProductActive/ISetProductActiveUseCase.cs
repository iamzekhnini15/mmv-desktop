using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.SetProductActive;

/// <summary>
/// Cas d'utilisation « Activer / désactiver un produit » (P3-4B). Alternative non destructive à la suppression :
/// un produit utilisé dans l'historique est désactivé plutôt que supprimé.
/// </summary>
public interface ISetProductActiveUseCase
{
    /// <summary>
    /// Applique l'état d'activation décrit par <paramref name="command"/>. Si le produit est introuvable, aucune
    /// écriture n'est effectuée (<see cref="SetProductActiveResult.ProductFound"/> = <c>false</c>). Idempotent :
    /// si l'état cible est déjà celui du produit, aucune écriture n'a lieu.
    /// </summary>
    Task<SetProductActiveResult> ExecuteAsync(SetProductActiveCommand command, CancellationToken cancellationToken = default);
}
