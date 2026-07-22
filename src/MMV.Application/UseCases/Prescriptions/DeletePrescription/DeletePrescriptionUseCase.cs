using System;
using System.Threading;
using System.Threading.Tasks;
using MMV.Domain.Exceptions;
using MMV.Domain.Interfaces.Repositories;
using MMV.Domain.Policies;

namespace MMV.Application.UseCases.Prescriptions.DeletePrescription;

/// <summary>
/// Implémentation du use case « Supprimer une ordonnance » (P2C-4), <b>devenu un point de refus</b> en P3-12 :
/// il oppose désormais la règle de conservation portée par <see cref="PrescriptionRetentionPolicy"/> au lieu de
/// supprimer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Règle métier (P3-12).</b> La suppression physique d'une ordonnance <b>existante</b> est refusée par une
/// <see cref="BusinessRuleException"/> (convention ADR §8 / P3-1 : refus dur = exception typée), sans
/// <c>DeleteAsync</c> ni <c>SaveChangesAsync</c>. Le refus est <b>inconditionnel</b> : aucun état, aucune date et
/// aucun indice d'usage ne le lève. Le motif est détaillé sur la politique — en résumé, aucune clé étrangère ne
/// relie une vente à l'ordonnance qui l'a motivée, donc « inutilisée » n'est pas une propriété décidable, et
/// l'ordonnance est un enregistrement médical source.
/// </para>
/// <para>
/// <b>Ce que ce use case reste.</b> Il est conservé — et non supprimé — parce que trois ViewModels le résolvent
/// par DI et que <c>CustomerPrescriptionsViewModel.ExecuteDeleteAsync</c> l'appelle. Le retirer casserait la
/// composition de l'UI, hors périmètre P3-12 (backend uniquement). Il devient donc le <b>gardien</b> du chemin
/// public : ce qui protégeait la donnée était jusqu'ici une boîte de dialogue de confirmation ; c'est désormais
/// cette règle, opposable à tout appelant.
/// </para>
/// <para>
/// <b>Comportement introuvable, inchangé.</b> Si l'ordonnance n'existe pas, le use case renvoie
/// <see cref="DeletePrescriptionResult.PrescriptionFound"/> = <c>false</c> sans écrire ni lever — contrat P2C-4
/// préservé tel quel. Ce cas n'est <b>pas</b> un refus métier : il décrit une ordonnance déjà absente (supprimée
/// depuis un autre poste avant P3-12, ou identifiant périmé), que la ViewModel traite par un rechargement depuis
/// la source. Le distinguer du refus évite de transformer un état multi-poste banal en erreur métier.
/// </para>
/// <para>
/// <b>Frontière transactionnelle.</b> Aucune écriture n'est plus possible : ni <c>ITransactionRunner</c>, ni
/// <c>SaveChangesAsync</c>. <c>IUnitOfWork</c> a été <b>retiré du constructeur</b> : depuis P3-12 ce use case
/// n'écrit plus, donc aucune unité de travail n'est à valider. Conserver la dépendance — même inutilisée —
/// aurait suggéré qu'une écriture reste possible ; la retirer rend l'absence d'écriture <b>structurelle</b> et
/// non plus seulement comportementale. Les seuls appelants étaient la DI (résolution automatique par
/// <c>AddScoped</c>) et les tests ; aucun appelant runtime ne construit ce use case manuellement.
/// </para>
/// </remarks>
public sealed class DeletePrescriptionUseCase : IDeletePrescriptionUseCase
{
    private readonly IPrescriptionRepository _prescriptionRepository;

    /// <param name="prescriptionRepository">
    /// Seule dépendance : elle sert uniquement à distinguer « ordonnance déjà absente » (contrat P2C-4) de
    /// « ordonnance existante, donc conservée » (règle P3-12). Aucune primitive d'écriture n'est atteignable.
    /// </param>
    public DeletePrescriptionUseCase(IPrescriptionRepository prescriptionRepository)
    {
        _prescriptionRepository = prescriptionRepository ?? throw new ArgumentNullException(nameof(prescriptionRepository));
    }

    /// <inheritdoc />
    public async Task<DeletePrescriptionResult> ExecuteAsync(DeletePrescriptionCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        // Lecture seule : elle ne sert qu'à distinguer « déjà absente » (contrat P2C-4) de « existante, donc
        // conservée » (règle P3-12). Aucune des deux branches n'écrit.
        var prescription = await _prescriptionRepository.GetByIdAsync(command.PrescriptionId, cancellationToken);
        if (prescription is null)
            return new DeletePrescriptionResult { PrescriptionFound = false, PrescriptionId = command.PrescriptionId };

        // P3-12 : refus dur, avant toute mutation. Ni DeleteAsync, ni SaveChangesAsync, ni SQL brut.
        throw new BusinessRuleException(PrescriptionRetentionPolicy.PhysicalDeletionForbiddenMessage);
    }
}
