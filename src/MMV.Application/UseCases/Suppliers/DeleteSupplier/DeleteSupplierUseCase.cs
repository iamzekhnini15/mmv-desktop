using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Persistence;
using MMV.Domain.Interfaces.Repositories;

namespace MMV.Application.UseCases.Suppliers.DeleteSupplier;

/// <summary>
/// Implémentation du use case « Supprimer un fournisseur », sécurisé en P3-9 : la suppression n'est autorisée que
/// si <b>aucun produit</b> ne référence le fournisseur, et le refus produit un <b>message métier stable</b> au lieu
/// d'une exception technique.
/// </summary>
/// <remarks>
/// <para>
/// <b>Le défaut corrigé.</b> Ce use case supprimait sans aucune vérification. La clé étrangère
/// <c>Product → Supplier</c> étant en <c>Restrict</c>, la base refusait bien l'opération — sans jamais produire
/// d'orphelin ni de cascade — mais l'échec remontait en <c>DbUpdateException</c> brute jusqu'à l'UI, qui affichait
/// littéralement « <i>An error occurred while saving the entity changes. See the inner exception for details.</i> »
/// après avoir fait confirmer « ⚠️ Cette action est irréversible ! ». Action légitime, refus correct, explication
/// nulle — et violation directe d'<c>adr-application-boundaries</c> (aucun message provider ne franchit la
/// frontière). Audit P3-9 §12, §16, 🔴-1.
/// </para>
/// <para>
/// <b>Suppression conditionnelle atomique, pas « check-then-act ».</b> Le flux ne lit pas pour décider :
/// <see cref="ISupplierRepository.TryDeleteIfUnusedAsync"/> évalue « aucun produit lié » et supprime dans la
/// <b>même instruction SQL</b>. Un produit créé sur un autre poste juste après une vérification préalable ne peut
/// donc pas être manqué — le TOCTOU du motif <c>DeleteCustomerUseCase</c> (P3-2B) est fermé, pas seulement
/// documenté. Les produits <b>inactifs comptent</b> : ils gardent leur ligne, donc leur FK.
/// </para>
/// <para>
/// <b>La lecture ne décide jamais de l'écriture.</b> Après un <c>false</c>, une lecture fraîche
/// (<see cref="ISupplierRepository.ExistsFreshAsync"/>) sert <b>uniquement à diagnostiquer</b> lequel des deux cas
/// s'est produit — fournisseur absent (⇒ <c>SupplierFound = false</c>, contrat inchangé et rejeu idempotent) ou
/// fournisseur encore référencé (⇒ refus métier). Elle ne peut plus rien supprimer : la décision est déjà prise et
/// close.
/// </para>
/// <para>
/// <b>Frontière transactionnelle et filet FK.</b> L'instruction atomique est enveloppée dans
/// <see cref="ITransactionRunner"/> non pour la rendre atomique — elle l'est déjà — mais parce que le runner est
/// le <b>seul</b> mécanisme du dépôt qui traduit une erreur de persistance en <c>PersistenceException</c> neutre
/// via <c>PersistenceErrorMapper</c>. La FK <c>Restrict</c> reste le filet ultime (chemin de suppression
/// alternatif, régression de configuration) : si elle se déclenchait, la violation de contrainte serait convertie
/// en <b>exactement le même</b> <see cref="SupplierHasProductsMessage"/> que la garde atomique. Chemin normal et
/// chemin concurrent produisent un résultat public identique ; aucun message EF/SQLite n'est jamais exposé. Les
/// autres catégories de persistance restent propagées, pour ne pas masquer une panne réelle.
/// </para>
/// <para>
/// <b>Aucune désactivation, aucun archivage.</b> Un fournisseur sans produit ne porte aucun historique — c'est sa
/// seule relation — donc sa suppression physique ne détruit rien et reste autorisée. Ajouter un <c>IsActive</c>
/// supposerait un besoin métier qu'aucun document, écran ni commentaire du dépôt n'exprime (audit §13.8).
/// </para>
/// </remarks>
public sealed class DeleteSupplierUseCase : IDeleteSupplierUseCase
{
    /// <summary>
    /// Message métier stable renvoyé lorsqu'un fournisseur encore référencé par au moins un produit ne peut pas
    /// être supprimé. Exposé en constante pour que l'UI et les tests s'y réfèrent sans le dupliquer (calque de
    /// <c>DeleteCustomerUseCase.CustomerHasHistoryMessage</c>).
    /// </summary>
    public const string SupplierHasProductsMessage =
        "Ce fournisseur ne peut pas être supprimé car il est associé à un ou plusieurs produits.";

    private readonly ISupplierRepository _supplierRepository;
    private readonly ITransactionRunner _transactionRunner;

    public DeleteSupplierUseCase(ISupplierRepository supplierRepository, ITransactionRunner transactionRunner)
    {
        _supplierRepository = supplierRepository ?? throw new ArgumentNullException(nameof(supplierRepository));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public async Task<DeleteSupplierResult> ExecuteAsync(DeleteSupplierCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        bool deleted;
        try
        {
            deleted = await _transactionRunner.RunAsync(
                ct => _supplierRepository.TryDeleteIfUnusedAsync(command.SupplierId, ct),
                cancellationToken);
        }
        catch (PersistenceException ex) when (ex.Category == PersistenceErrorCategory.ConstraintViolation)
        {
            // Filet FK : la condition atomique rend ce chemin normalement inatteignable, mais si la contrainte
            // d'intégrité se déclenchait malgré tout, l'utilisateur doit lire la MÊME règle métier — jamais le
            // message du provider. Seule la catégorie « contrainte » est convertie ; les autres remontent.
            throw new BusinessRuleException(SupplierHasProductsMessage);
        }

        if (deleted)
            return new DeleteSupplierResult { SupplierFound = true, SupplierId = command.SupplierId };

        // Diagnostic UNIQUEMENT : l'écriture est déjà tranchée. Lecture fraîche (AnyAsync), jamais le change
        // tracker — après une suppression concurrente, FindAsync renverrait une entité suivie et périmée.
        var stillExists = await _supplierRepository.ExistsFreshAsync(command.SupplierId, cancellationToken);
        if (!stillExists)
            return new DeleteSupplierResult { SupplierFound = false, SupplierId = command.SupplierId };

        throw new BusinessRuleException(SupplierHasProductsMessage);
    }
}
