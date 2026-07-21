using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Application.Common;
using MMV.Application.UseCases.Suppliers.Common;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Validators;

namespace MMV.Application.UseCases.Suppliers.UpdateSupplier;

/// <summary>
/// Implémentation du use case « Modifier un fournisseur » (P2C-GLOBAL). Déplace, <b>sans changement de comportement
/// observable</b>, la mise à jour qui vivait dans la branche édition de <c>SupplierFormViewModel.SaveAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>P3-9 — validation sur un candidat détaché, AVANT de toucher l'entité suivie.</b> L'ordre est délibéré :
/// normaliser, construire un candidat détaché, valider, <b>puis seulement</b> charger et muter. Valider après
/// mutation laisserait une entité suivie porteuse de valeurs refusées dans le <c>DbContext</c> de la portée —
/// qu'un <c>SaveChangesAsync</c> ultérieur, déclenché ailleurs, persisterait silencieusement.
/// </para>
/// <para>
/// <b>Aucun patch partiel.</b> Les cinq champs sont remplacés par la commande complète : une commande
/// partiellement remplie <b>efface</b> bien l'e-mail, le téléphone, l'adresse et le code. C'est le comportement
/// historique, conservé sciemment — il n'existe aucune sémantique « champ non fourni = ne pas toucher », et en
/// inventer une changerait le contrat des appelants. Le <b>lost update</b> qui en découle (deux postes éditant la
/// même fiche : le dernier écrase l'autre) n'est <b>pas</b> traité en P3-9 : c'est une dette transverse à toutes
/// les entités du dépôt (aucun <c>RowVersion</c> nulle part), et la corriger pour le seul fournisseur serait
/// incohérent. Report explicite — audit P3-9 §20 🟠-3.
/// </para>
/// <para>
/// Charge le fournisseur par son identifiant : s'il n'existe pas, renvoie <c>SupplierFound = false</c> sans
/// écrire. Mono-écriture (Update + <c>SaveChangesAsync</c> unique), <c>ITransactionRunner</c> non requis.
/// </para>
/// </remarks>
public sealed class UpdateSupplierUseCase : IUpdateSupplierUseCase
{
    private static readonly SupplierValidator SupplierValidator = new();

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

        // P3-9 : même normaliseur que la création — un seul propriétaire pour les deux chemins d'écriture.
        var normalized = SupplierInputNormalizer.Normalize(
            command.Name, command.ContactEmail, command.Phone, command.Address, command.ReferenceCode);

        var supplier = await _supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null)
            return new UpdateSupplierResult { SupplierFound = false, SupplierId = command.SupplierId };

        // P3-1 : validation sur un candidat DÉTACHÉ. L'entité suivie ci-dessus n'est PAS encore touchée — c'est
        // l'invariant qui compte, et il est ici garanti structurellement : les valeurs candidates ne peuvent pas
        // atteindre le change tracker avant d'avoir été acceptées.
        // Le chargement précède la validation pour que SupplierFound reste une information HONNÊTE : le refuser
        // après validation obligerait à affirmer une existence jamais vérifiée. Ordre identique à
        // UpdateProductUseCase, et les deux axes du résultat restent indépendants (SupplierFound / IsValid).
        var validationErrors = CommandValidation.Validate(SupplierValidator, normalized.ToCandidate(command.SupplierId));
        if (validationErrors.Count > 0)
            return new UpdateSupplierResult
            {
                SupplierFound = true,
                SupplierId = command.SupplierId,
                ValidationErrors = validationErrors,
            };

        normalized.ApplyTo(supplier);

        await _supplierRepository.UpdateAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateSupplierResult { SupplierFound = true, SupplierId = supplier.SupplierId };
    }
}
