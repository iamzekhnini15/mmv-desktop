using System.Threading;
using System.Threading.Tasks;

namespace MMV.Application.UseCases.Products.CreateProduct;

/// <summary>
/// Cas d'utilisation « Créer un produit » (P2C-GLOBAL). Remplace la branche création auparavant portée par
/// <c>ProductFormViewModel.ExecuteSaveAsync</c> (création du produit et de ses détails spécifiques à la catégorie).
/// </summary>
public interface ICreateProductUseCase
{
    /// <summary>Crée le produit décrit par <paramref name="command"/> et renvoie son identifiant.</summary>
    Task<CreateProductResult> ExecuteAsync(CreateProductCommand command, CancellationToken cancellationToken = default);
}
