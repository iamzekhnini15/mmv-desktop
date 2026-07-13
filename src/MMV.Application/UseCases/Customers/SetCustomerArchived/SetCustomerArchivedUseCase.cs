using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Customers.SetCustomerArchived;

/// <summary>
/// Implémentation du use case « Archiver / réactiver un client » (P3-2B). Charge le client, applique l'état
/// d'archivage cible via les méthodes métier du domaine (<c>Customer.Archive</c> / <c>Customer.Reactivate</c>),
/// puis persiste.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aucune destruction.</b> L'archivage est un simple <c>UPDATE</c> d'un booléen : l'historique du client
/// (ordonnances, ventes) est intégralement conservé, et l'opération est réversible par réactivation.
/// </para>
/// <para>
/// <b>Idempotence stricte.</b> Si le client est déjà dans l'état demandé, le use case renvoie un succès
/// <b>sans aucune écriture</b> : ni <c>UpdateAsync</c>, ni <c>SaveChangesAsync</c>, et <c>UpdatedAt</c> reste
/// <b>inchangé</b> (il continue de refléter la dernière modification réelle de la fiche, et non un clic sans
/// effet).
/// </para>
/// <para>
/// <b>Multi-poste.</b> La commande porte un état <b>absolu</b> (et non un basculement) : deux postes qui
/// archivent le même client convergent vers le même état, et le second n'écrit rien. Sûr face à la concurrence
/// sans jeton de concurrence (cf. ADR-PROD-DB-001).
/// </para>
/// <para>
/// <b>Frontière transactionnelle.</b> Mono-écriture (<c>UpdateAsync</c> + un unique <c>SaveChangesAsync</c>) :
/// <c>ITransactionRunner</c> n'est pas requis, cohérent avec <c>SetUserActiveUseCase</c> et
/// <c>UpdateCustomerUseCase</c>.
/// </para>
/// <para>
/// <b>Horodatage.</b> <c>UpdatedAt</c> est renseigné par l'Application (convention du dépôt : l'entité ne lit
/// jamais l'horloge dans ses méthodes métier), comme dans <c>UpdateCustomerUseCase</c>.
/// </para>
/// </remarks>
public sealed class SetCustomerArchivedUseCase : ISetCustomerArchivedUseCase
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

    public SetCustomerArchivedUseCase(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<SetCustomerArchivedResult> ExecuteAsync(SetCustomerArchivedCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var customer = await _customerRepository.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer is null)
            return new SetCustomerArchivedResult { CustomerFound = false, CustomerId = command.CustomerId };

        // Idempotence stricte : l'état cible est déjà celui du client → succès sans AUCUNE écriture (ni
        // UpdateAsync, ni SaveChangesAsync) et sans toucher UpdatedAt, qui doit continuer de refléter la
        // dernière modification RÉELLE de la fiche.
        if (customer.IsArchived == command.IsArchived)
        {
            return new SetCustomerArchivedResult
            {
                CustomerFound = true,
                CustomerId = command.CustomerId,
                IsArchived = command.IsArchived
            };
        }

        if (command.IsArchived)
        {
            customer.Archive();
        }
        else
        {
            customer.Reactivate();
        }

        customer.UpdatedAt = DateTime.UtcNow;

        await _customerRepository.UpdateAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SetCustomerArchivedResult
        {
            CustomerFound = true,
            CustomerId = command.CustomerId,
            IsArchived = command.IsArchived
        };
    }
}
