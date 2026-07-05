using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.DeleteProduct;

/// <summary>
/// Cas d'utilisation « Supprimer un produit » (P2C-GLOBAL). Remplace l'orchestration auparavant portée par
/// <c>ProductsListViewModel.ConfirmAndDeleteProductAsync</c> (appel direct <c>IProductRepository.DeleteAsync</c> +
/// <c>IUnitOfWork.SaveChangesAsync</c>).
/// </summary>
public interface IDeleteProductUseCase
{
    /// <summary>
    /// Supprime le produit décrit par <paramref name="command"/>. Si le produit est introuvable, aucune écriture
    /// n'est effectuée (<see cref="DeleteProductResult.ProductFound"/> = <c>false</c>).
    /// </summary>
    Task<DeleteProductResult> ExecuteAsync(DeleteProductCommand command, CancellationToken cancellationToken = default);
}
