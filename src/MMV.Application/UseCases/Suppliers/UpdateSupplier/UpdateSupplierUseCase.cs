using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Suppliers.UpdateSupplier;

/// <summary>
/// Implémentation du use case « Modifier un fournisseur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la mise à jour qui vivait dans la branche édition de <c>SupplierFormViewModel.SaveAsync</c>.
/// </summary>
/// <remarks>
/// Charge d'abord le fournisseur par son identifiant : s'il n'existe pas, renvoie <c>SupplierFound = false</c> sans
/// écrire. Mono-écriture (Update + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.
/// </remarks>
public sealed class UpdateSupplierUseCase : IUpdateSupplierUseCase
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateSupplierUseCase(ISupplierRepository supplierRepository, IUnitOfWork unitOfWork)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<UpdateSupplierResult> ExecuteAsync(UpdateSupplierCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var supplier = await _supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null)
            return new UpdateSupplierResult { SupplierFound = false, SupplierId = command.SupplierId };

        supplier.Name = command.Name;
        supplier.ContactEmail = command.ContactEmail;
        supplier.Phone = command.Phone;
        supplier.Address = command.Address;
        supplier.ReferenceCode = command.ReferenceCode;

        await _supplierRepository.UpdateAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateSupplierResult { SupplierFound = true, SupplierId = supplier.SupplierId };
    }
}
