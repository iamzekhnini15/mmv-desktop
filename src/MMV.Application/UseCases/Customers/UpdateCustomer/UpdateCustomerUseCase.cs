using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Customers.UpdateCustomer;

/// <summary>
/// Implémentation du use case « Modifier un client » (P2C-2). Déplace, <b>sans changement de comportement
/// observable</b>, l'orchestration de persistance qui vivait dans la branche édition de
/// <c>CustomerFormViewModel.ExecuteSave</c> vers la couche Application. Réutilise telles quelles les interfaces de
/// persistance existantes (<see cref="ICustomerRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue une <b>seule</b> écriture atomique
/// (<c>UpdateAsync</c> + un unique <c>SaveChangesAsync</c>) sans <c>ITransactionRunner</c> : aucun runner de
/// transaction n'est introduit (déplacement iso-fonctionnel, pas refonte).
/// </para>
/// <para>
/// <b>Chargement.</b> Le client est rechargé par son identifiant (<see cref="ICustomerRepository.GetByIdAsync"/>)
/// puis ses champs éditables sont mis à jour, comme dans le flux d'origine (où l'entité <c>_originalCustomer</c>
/// chargée par la ViewModel était mutée puis persistée). <c>CreatedAt</c> est préservé ; seul <c>UpdatedAt</c> est
/// réaffecté à <see cref="DateTime.UtcNow"/>.
/// </para>
/// <para>
/// <b>Comportement introuvable.</b> En mode édition, la ViewModel ouvre toujours le formulaire avec un client
/// réel ; le cas « introuvable » ne survient donc pas en pratique. Le use case adopte un contrat sûr et explicite
/// (cohérent avec <c>UpdateOrderUseCase</c>) : si le client n'existe pas, il renvoie
/// <see cref="UpdateCustomerResult.CustomerFound"/> = <c>false</c> sans écrire.
/// </para>
/// </remarks>
public sealed class UpdateCustomerUseCase : IUpdateCustomerUseCase
{
    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication, cf. ADR frontières §9).
    // Stateless et thread-safe : une seule instance partagée suffit.
    private static readonly CustomerValidator CustomerValidator = new();

    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCustomerUseCase(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<UpdateCustomerResult> ExecuteAsync(UpdateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var customer = await _customerRepository.GetByIdAsync(command.CustomerId, cancellationToken);
        if (customer is null)
            return new UpdateCustomerResult { CustomerFound = false, CustomerId = command.CustomerId };

        // Mise à jour des mêmes champs éditables que le flux d'origine (champs optionnels normalisés en null si
        // blancs ; UpdatedAt réaffecté, CreatedAt préservé).
        customer.FirstName = command.FirstName;
        customer.LastName = command.LastName;
        customer.Email = NullIfBlank(command.Email);
        customer.Phone = NullIfBlank(command.Phone);
        customer.BirthDate = command.BirthDate;
        customer.Address = NullIfBlank(command.Address);
        customer.City = NullIfBlank(command.City);
        customer.PostalCode = NullIfBlank(command.PostalCode);
        customer.SocialSecurityNumber = NullIfBlank(command.SocialSecurityNumber);
        customer.InsuranceName = NullIfBlank(command.InsuranceName);
        customer.Notes = NullIfBlank(command.Notes);
        customer.UpdatedAt = DateTime.UtcNow;

        // P3-1 : validation de commande AVANT persistance. L'entité suivie est mutée mais aucune écriture n'est
        // demandée si elle est invalide (pas d'UpdateAsync ni de SaveChangesAsync) ⇒ le contexte à portée est
        // libéré sans persister : la ligne en base reste inchangée.
        var validationErrors = CommandValidation.Validate(CustomerValidator, customer);
        if (validationErrors.Count > 0)
            return new UpdateCustomerResult
            {
                CustomerFound = true,
                CustomerId = command.CustomerId,
                ValidationErrors = validationErrors
            };

        await _customerRepository.UpdateAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateCustomerResult
        {
            CustomerFound = true,
            CustomerId = customer.CustomerId,
            DisplayName = $"{customer.FirstName} {customer.LastName}".Trim()
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
