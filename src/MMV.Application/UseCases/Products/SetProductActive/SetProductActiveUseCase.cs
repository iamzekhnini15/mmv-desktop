using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Products.SetProductActive;

/// <summary>
/// Implémentation du use case « Activer / désactiver un produit » (P3-4B). Charge le produit, applique l'état
/// d'activation cible (valeur absolue), puis persiste. Modèle : <c>SetUserActiveUseCase</c> /
/// <c>SetCustomerArchivedUseCase</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Portée stricte.</b> Seul <c>IsActive</c> est modifié : ni le stock, ni les prix, ni les détails, ni la
/// référence. La réactivation est permise (état absolu). L'opération est un simple <c>UPDATE</c> booléen —
/// aucune destruction, entièrement réversible.
/// </para>
/// <para>
/// <b>Idempotence stricte.</b> Si le produit est déjà dans l'état demandé, le use case renvoie un succès
/// <b>sans aucune écriture</b> (ni <c>UpdateAsync</c>, ni <c>SaveChangesAsync</c>). Sûr en multi-poste : la
/// commande porte un état absolu, deux postes convergent vers le même état.
/// </para>
/// <para>
/// <b>Frontière transactionnelle.</b> Mono-écriture (<c>UpdateAsync</c> + un unique <c>SaveChangesAsync</c>) :
/// <c>ITransactionRunner</c> non requis (cohérent avec <c>SetUserActiveUseCase</c>).
/// </para>
/// </remarks>
public sealed class SetProductActiveUseCase : ISetProductActiveUseCase
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetProductActiveUseCase(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<SetProductActiveResult> ExecuteAsync(SetProductActiveCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var product = await _productRepository.GetByIdAsync(command.ProductId, cancellationToken);
        if (product is null)
            return new SetProductActiveResult { ProductFound = false, ProductId = command.ProductId };

        // Idempotence stricte : l'état cible est déjà celui du produit → succès sans aucune écriture.
        if (product.IsActive == command.IsActive)
            return new SetProductActiveResult { ProductFound = true, ProductId = command.ProductId, IsActive = command.IsActive };

        product.IsActive = command.IsActive;

        await _productRepository.UpdateAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetProductActiveResult { ProductFound = true, ProductId = command.ProductId, IsActive = command.IsActive };
    }
}
