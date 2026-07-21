using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Application.UseCases.Suppliers.Common;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Suppliers.CreateSupplier;

/// <summary>
/// Implémentation du use case « Créer un fournisseur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la création qui vivait dans la branche création de <c>SupplierFormViewModel.SaveAsync</c> vers
/// la couche Application. Réutilise telles quelles les interfaces de persistance existantes
/// (<see cref="ISupplierRepository"/>, <see cref="IUnitOfWork"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>P3-9 — normalisation puis validation, avant toute écriture.</b> L'entrée passe par le propriétaire commun
/// <see cref="SupplierInputNormalizer"/> (partagé avec <c>UpdateSupplierUseCase</c>), puis le candidat détaché est
/// validé par le <see cref="SupplierValidator"/> Domain via <see cref="CommandValidation"/>. Commande invalide ⇒
/// <b>aucun</b> <c>CreateAsync</c>, <b>aucun</b> <c>SaveChangesAsync</c>. Jusqu'ici ce use case n'exerçait
/// strictement aucun contrôle : un nom vide, un nom de 5 000 caractères ou un e-mail absurde étaient persistés
/// (audit P3-9 §16, vérifié empiriquement).
/// </para>
/// <para>
/// Une saisie invalide est une <b>erreur de saisie</b>, pas un refus dur : elle est renvoyée dans
/// <see cref="CreateSupplierResult.ValidationErrors"/>, jamais levée en <c>BusinessRuleException</c> (convention
/// P3-1, identique à <c>CreateProductUseCase</c>).
/// </para>
/// <para>
/// Mono-écriture (Create + <c>SaveChangesAsync</c> unique), intrinsèquement atomique : <c>ITransactionRunner</c>
/// n'est pas nécessaire (cohérent avec <c>CreateCustomerUseCase</c>). Aucune garde de doublon n'est ajoutée —
/// aucune unicité fournisseur n'est prouvée métier (audit §10, report explicite).
/// </para>
/// </remarks>
public sealed class CreateSupplierUseCase : ICreateSupplierUseCase
{
    // Validateur Domain réutilisé (règle métier propriétaire du Domain — aucune duplication). Stateless, partagé.
    private static readonly SupplierValidator SupplierValidator = new();

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

        // P3-9 : normalisation commune (Trim ; chaîne vide ⇒ null pour les champs optionnels) AVANT validation —
        // sans elle, « a@b.fr » entouré d'espaces échouerait EmailAddress().
        var normalized = SupplierInputNormalizer.Normalize(
            command.Name, command.ContactEmail, command.Phone, command.Address, command.ReferenceCode);

        // Candidat DÉTACHÉ : rien n'est ajouté au contexte tant que la validation n'a pas réussi.
        var supplier = normalized.ToCandidate();

        // P3-1 : validation de commande AVANT toute écriture.
        var validationErrors = CommandValidation.Validate(SupplierValidator, supplier);
        if (validationErrors.Count > 0)
            return new CreateSupplierResult { ValidationErrors = validationErrors };

        await _supplierRepository.CreateAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateSupplierResult { SupplierId = supplier.SupplierId, Name = supplier.Name };
    }
}
