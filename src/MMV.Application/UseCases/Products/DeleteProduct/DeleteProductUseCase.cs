using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.DeleteProduct;

/// <summary>
/// Implémentation du use case « Supprimer un produit » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la suppression qui vivait dans <c>ProductsListViewModel.ConfirmAndDeleteProductAsync</c>.
/// </summary>
/// <remarks>
/// Charge d'abord le produit par son identifiant : s'il n'existe pas, renvoie <c>ProductFound = false</c> sans
/// écrire. Mono-écriture (Delete + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis. La
/// suppression repose sur le comportement existant du repository / des cascades EF (aucune cascade nouvelle).
/// </remarks>
public sealed class DeleteProductUseCase : IDeleteProductUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteProductUseCase(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<DeleteProductResult> ExecuteAsync(DeleteProductCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return new DeleteProductResult { ProductFound = false, ProductId = command.ProductId };

        await _productRepository.DeleteAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteProductResult { ProductFound = true, ProductId = command.ProductId };
    }
}
