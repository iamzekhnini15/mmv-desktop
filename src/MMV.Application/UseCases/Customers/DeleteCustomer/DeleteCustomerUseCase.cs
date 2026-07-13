using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Customers.DeleteCustomer;

/// <summary>
/// Implémentation du use case « Supprimer un client » (P2C-3, sécurisé en P3-2B). Réutilise telles quelles les
/// interfaces de persistance existantes (<see cref="ICustomerRepository"/>, <see cref="IUnitOfWork"/>) et y
/// ajoute les deux requêtes d'existence légères nécessaires au garde-fou métier.
/// </summary>
/// <remarks>
/// <para>
/// <b>Règle métier (P3-2B).</b> La suppression physique n'est autorisée que si le client n'a <b>aucun
/// historique</b> — « historique » = au moins une ordonnance <b>ou</b> au moins une vente. Sinon la suppression
/// est <b>refusée</b> par une <see cref="BusinessRuleException"/> (convention ADR §8 / P3-1 : refus dur = exception
/// typée), sans <c>DeleteAsync</c> ni <c>SaveChangesAsync</c> : le client doit être archivé
/// (<c>SetCustomerArchivedUseCase</c>). Les ordonnances et les ventes sont <b>toujours</b> conservées.
/// </para>
/// <para>
/// <b>Multi-poste.</b> Ce contrôle applicatif produit un message métier clair mais reste un « check-then-act »
/// non atomique. Le rempart réel est la base : les deux clés étrangères <c>Prescription → Customer</c> et
/// <c>Sale → Customer</c> sont en <c>Restrict</c> (P3-2B), donc un <c>DELETE</c> concurrent d'un client dont
/// l'historique vient d'être créé par un autre poste échoue au niveau SQL. Cette violation de contrainte remonte
/// alors comme erreur de persistance, non comme <see cref="BusinessRuleException"/> (cf. rapport P3-2B §12).
/// </para>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux reste mono-écriture (une suppression, un <c>SaveChangesAsync</c>) :
/// <c>ITransactionRunner</c> n'est pas nécessaire (cohérent avec <c>DeleteOrderUseCase</c>). Les lectures
/// d'existence ne modifient rien.
/// </para>
/// </remarks>
public sealed class DeleteCustomerUseCase : IDeleteCustomerUseCase
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'un client porteur d'historique ne peut pas être supprimé. Exposé en
    /// constante pour que l'UI (P3-2C) et les tests s'y réfèrent sans le dupliquer.
    /// </summary>
    public const string CustomerHasHistoryMessage =
        "Ce client possède un historique et ne peut pas être supprimé. Archivez-le à la place.";

    private readonly ICustomerRepository _customerRepository;
    private readonly IPrescriptionRepository _prescriptionRepository;
    private readonly ISaleRepository _saleRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCustomerUseCase(
        ICustomerRepository customerRepository,
        IPrescriptionRepository prescriptionRepository,
        ISaleRepository saleRepository,
        IUnitOfWork unitOfWork)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
        _saleRepository = saleRepository ?? throw new ArgumentNullException(nameof(saleRepository));
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

        // Garde-fou métier : existence légère (AnyAsync), aucune collection matérialisée.
        var hasPrescriptions = await _prescriptionRepository.ExistsByCustomerIdAsync(command.CustomerId, cancellationToken);
        var hasSales = await _saleRepository.ExistsByCustomerIdAsync(command.CustomerId, cancellationToken);

        if (hasPrescriptions || hasSales)
            throw new BusinessRuleException(CustomerHasHistoryMessage);

        await _customerRepository.DeleteAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DeleteCustomerResult { CustomerFound = true, CustomerId = command.CustomerId };
    }
}
