using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Customers.DeleteCustomer;

/// <summary>
/// Implémentation du use case « Supprimer un client » (P2C-3). Déplace, <b>sans changement de comportement
/// observable</b>, la suppression qui vivait dans <c>CustomersListViewModel.ExecuteDelete</c> vers la couche
/// Application. Réutilise telles quelles les interfaces de persistance existantes
/// (<see cref="ICustomerRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue une seule suppression suivie d'un seul
/// <c>SaveChangesAsync</c> — opération mono-écriture intrinsèquement atomique. <c>ITransactionRunner</c> n'est
/// donc <b>pas</b> nécessaire ici (cohérent avec <c>DeleteOrderUseCase</c>). Cette absence est volontaire et
/// documentée.
/// </para>
/// <para>
/// <b>Comportement introuvable.</b> Le flux d'origine appelait <c>DeleteAsync(id)</c> sans vérifier l'existence
/// préalable. Ce use case charge d'abord l'entité par son identifiant : si le client n'existe pas, il renvoie
/// <see cref="DeleteCustomerResult.CustomerFound"/> = <c>false</c> sans écrire (contrat sûr et explicite,
/// cohérent avec <c>DeleteOrderUseCase</c>). Aucune cascade métier nouvelle n'est introduite : la suppression
/// repose sur le comportement existant du repository / des cascades EF.
/// </para>
/// </remarks>
public sealed class DeleteCustomerUseCase : IDeleteCustomerUseCase
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCustomerUseCase(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<DeleteCustomerResult> ExecuteAsync(DeleteCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Charger l'entité avant suppression : permet de reporter CustomerFound précisément et d'éviter une
        // écriture dangereuse quand l'identifiant est introuvable.
        var customer = await _customerRepository.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer is null)
            return new DeleteCustomerResult { CustomerFound = false, CustomerId = command.CustomerId };

        await _customerRepository.DeleteAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteCustomerResult { CustomerFound = true, CustomerId = command.CustomerId };
    }
}
