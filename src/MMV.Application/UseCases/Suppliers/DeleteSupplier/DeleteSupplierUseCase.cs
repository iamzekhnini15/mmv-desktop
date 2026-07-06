using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Suppliers.DeleteSupplier;

/// <summary>
/// Implémentation du use case « Supprimer un fournisseur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la suppression qui vivait dans <c>SuppliersViewModel.OnDetailDeleteRequested</c>.
/// </summary>
/// <remarks>
/// Charge d'abord le fournisseur par son identifiant : s'il n'existe pas, renvoie <c>SupplierFound = false</c> sans
/// écrire (contrat sûr et explicite, cohérent avec <c>DeleteCustomerUseCase</c>). Mono-écriture (Delete +
/// <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class DeleteSupplierUseCase : IDeleteSupplierUseCase
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteSupplierUseCase(ISupplierRepository supplierRepository, IUnitOfWork unitOfWork)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<DeleteSupplierResult> ExecuteAsync(DeleteSupplierCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var supplier = await _supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null)
            return new DeleteSupplierResult { SupplierFound = false, SupplierId = command.SupplierId };

        await _supplierRepository.DeleteAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteSupplierResult { SupplierFound = true, SupplierId = command.SupplierId };
    }
}
