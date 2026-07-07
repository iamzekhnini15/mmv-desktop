using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Domain.Entities;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Customers.CreateCustomer;

/// <summary>
/// Implémentation du use case « Créer un client » (P2C-2). Déplace, <b>sans changement de comportement
/// observable</b>, l'orchestration de persistance qui vivait dans la branche création de
/// <c>CustomerFormViewModel.ExecuteSave</c> vers la couche Application. Réutilise telles quelles les interfaces de
/// persistance existantes (<see cref="ICustomerRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Frontière transactionnelle.</b> Le flux d'origine effectue une <b>seule</b> écriture atomique
/// (<c>CreateAsync</c> + un unique <c>SaveChangesAsync</c>) sans <c>ITransactionRunner</c> : il n'y a pas de
/// séquence multi-étapes à protéger, donc aucun runner de transaction n'est introduit (déplacement iso-fonctionnel,
/// pas refonte).
/// </para>
/// <para>
/// <b>Normalisation.</b> Les champs optionnels blancs sont normalisés vers <c>null</c> et les horodatages
/// <c>CreatedAt</c>/<c>UpdatedAt</c> sont fixés à <see cref="DateTime.UtcNow"/>, exactement comme le flux d'origine.
/// Aucune nouvelle règle métier ni validation lourde n'est ajoutée (la validation de surface — prénom/nom requis —
/// reste côté ViewModel).
/// </para>
/// </remarks>
public sealed class CreateCustomerUseCase : ICreateCustomerUseCase
{
    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication, cf. ADR frontières §9).
    // Stateless et thread-safe : une seule instance partagée suffit.
    private static readonly CustomerValidator CustomerValidator = new();

    private readonly ICustomerRepository _customerRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCustomerUseCase(ICustomerRepository customerRepository, IUnitOfWork unitOfWork)
    {
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <inheritdoc />
    public async Task<CreateCustomerResult> ExecuteAsync(CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        var now = DateTime.UtcNow;
        var customer = new Customer
        {
            FirstName = command.FirstName,
            LastName = command.LastName,
            Email = NullIfBlank(command.Email),
            Phone = NullIfBlank(command.Phone),
            BirthDate = command.BirthDate,
            Address = NullIfBlank(command.Address),
            City = NullIfBlank(command.City),
            PostalCode = NullIfBlank(command.PostalCode),
            SocialSecurityNumber = NullIfBlank(command.SocialSecurityNumber),
            InsuranceName = NullIfBlank(command.InsuranceName),
            Notes = NullIfBlank(command.Notes),
            CreatedAt = now,
            UpdatedAt = now
        };

        // P3-1 : validation de commande AVANT toute écriture. On valide le candidat normalisé (les blancs → null,
        // exactement ce qui serait persisté) avec le validateur Domain. Commande invalide ⇒ aucun accès dépôt.
        var validationErrors = CommandValidation.Validate(CustomerValidator, customer);
        if (validationErrors.Count > 0)
            return new CreateCustomerResult { ValidationErrors = validationErrors };

        await _customerRepository.CreateAsync(customer, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateCustomerResult
        {
            CustomerId = customer.CustomerId,
            DisplayName = $"{customer.FirstName} {customer.LastName}".Trim()
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
