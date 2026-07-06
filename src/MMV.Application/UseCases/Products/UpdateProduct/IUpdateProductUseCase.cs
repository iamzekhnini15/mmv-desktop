using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.UpdateProduct;

/// <summary>
/// Cas d'utilisation « Modifier un produit » (P2C-GLOBAL). Remplace la branche édition auparavant portée par
/// <c>ProductFormViewModel.ExecuteSaveAsync</c>.
/// </summary>
public interface IUpdateProductUseCase
{
    /// <summary>
    /// Applique les modifications décrites par <paramref name="command"/>. Si le produit est introuvable, aucune
    /// écriture n'est effectuée (<see cref="UpdateProductResult.ProductFound"/> = <c>false</c>).
    /// </summary>
    Task<UpdateProductResult> ExecuteAsync(UpdateProductCommand command, CancellationToken cancellationToken = default);
}
