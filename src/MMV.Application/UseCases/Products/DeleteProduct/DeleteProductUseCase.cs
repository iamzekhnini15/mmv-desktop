using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.DeleteProduct;

/// <summary>
/// Implémentation du use case « Supprimer un produit », sécurisée en P3-4B sur le modèle de
/// <c>DeleteCustomerUseCase</c> : la suppression physique n'est autorisée que si le produit n'est utilisé par
/// <b>aucune</b> ligne d'historique (vente, commande ou mouvement de stock).
/// </summary>
/// <remarks>
/// <para>
/// <b>Règle métier (P3-4B).</b> Si le produit est référencé par au moins un <c>SaleItem</c>, <c>OrderItem</c> ou
/// <c>StockMovement</c>, la suppression est <b>refusée</b> par une <see cref="BusinessRuleException"/> (convention
/// ADR §8 / P3-1 : refus dur = exception typée), sans <c>DeleteAsync</c> ni <c>SaveChangesAsync</c> : le produit
/// doit être désactivé (<c>SetProductActiveUseCase</c>). Aucune ligne d'historique n'est touchée. Un produit
/// réellement inutilisé peut encore être supprimé physiquement (ses détails 1-1 partent en cascade — ils lui
/// appartiennent et ne constituent pas un historique indépendant).
/// </para>
/// <para>
/// <b>Multi-poste.</b> Ce contrôle applicatif produit un message métier clair mais reste un « check-then-act » non
/// atomique. Le rempart réel est la base : les FK <c>SaleItem</c>/<c>OrderItem</c>/<c>StockMovement → Product</c>
/// passent en <c>Restrict</c> (P3-4B), donc un <c>DELETE</c> concurrent d'un produit dont l'historique vient d'être
/// créé par un autre poste échoue au niveau SQL et remonte comme erreur de persistance, non comme
/// <see cref="BusinessRuleException"/>.
/// </para>
/// </remarks>
public sealed class DeleteProductUseCase : IDeleteProductUseCase
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'un produit porteur d'historique ne peut pas être supprimé. Exposé en
    /// constante pour que l'UI et les tests s'y réfèrent sans le dupliquer.
    /// </summary>
    public const string ProductInUseMessage =
        "Ce produit est utilisé dans l'historique et ne peut pas être supprimé. Désactivez-le à la place.";

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

        // Garde-fou métier : existence légère (AnyAsync à court-circuit), aucune collection matérialisée.
        var isUsed = await _productRepository.IsReferencedByHistoryAsync(command.ProductId, cancellationToken);
        if (isUsed)
            throw new BusinessRuleException(ProductInUseMessage);

        await _productRepository.DeleteAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteProductResult { ProductFound = true, ProductId = command.ProductId };
    }
}
