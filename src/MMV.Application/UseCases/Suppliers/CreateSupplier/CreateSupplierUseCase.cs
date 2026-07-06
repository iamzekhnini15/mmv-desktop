using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Suppliers.CreateSupplier;

/// <summary>
/// Implémentation du use case « Créer un fournisseur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la création qui vivait dans la branche création de <c>SupplierFormViewModel.SaveAsync</c> vers
/// la couche Application. Réutilise telles quelles les interfaces de persistance existantes
/// (<see cref="ISupplierRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// Mono-écriture (Create + <c>SaveChangesAsync</c> unique), intrinsèquement atomique : <c>ITransactionRunner</c>
/// n'est pas nécessaire (cohérent avec <c>CreateCustomerUseCase</c>).
/// </remarks>
public sealed class CreateSupplierUseCase : ICreateSupplierUseCase
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateSupplierUseCase(ISupplierRepository supplierRepository, IUnitOfWork unitOfWork)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<CreateSupplierResult> ExecuteAsync(CreateSupplierCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var supplier = new Supplier
        {
            Name = command.Name,
            ContactEmail = command.ContactEmail,
            Phone = command.Phone,
            Address = command.Address,
            ReferenceCode = command.ReferenceCode,
        };

        await _supplierRepository.CreateAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateSupplierResult { SupplierId = supplier.SupplierId, Name = supplier.Name };
    }
}
