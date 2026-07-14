using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Prescriptions.CreatePrescription;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreatePrescriptionUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (événement <c>PrescriptionSaved</c>, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// <para>
/// <b>P3-3B.</b> Le résultat distingue désormais trois issues sans jamais recourir à une exception pour un cas
/// nominal (convention P3-1, identique à <c>CreateCustomerResult</c> / <c>UpdateCustomerResult</c>) :
/// <list type="bullet">
///   <item>commande <b>invalide</b> ⇒ <see cref="ValidationErrors"/> renseignée, <see cref="IsValid"/> = <c>false</c>,
///         <see cref="PrescriptionId"/> = 0, <b>aucune</b> écriture ;</item>
///   <item>client <b>introuvable</b> ⇒ <see cref="CustomerFound"/> = <c>false</c>, <b>aucune</b> écriture (la clé
///         étrangère n'est plus laissée produire une <c>DbUpdateException</c> comme chemin nominal) ;</item>
///   <item>succès ⇒ <see cref="IsValid"/> et <see cref="CustomerFound"/> à <c>true</c>, identifiant attribué.</item>
/// </list>
/// </para>
/// <para>
/// Le refus « client <b>archivé</b> » n'est <b>pas</b> porté par ce résultat : c'est un refus métier <b>dur</b>, levé
/// en <c>BusinessRuleException</c> (ADR §8 / patron P3-2B).
/// </para>
/// </remarks>
public sealed class CreatePrescriptionResult
{
    /// <summary>Identifiant de l'ordonnance créée (0 si la commande a été refusée ou le client introuvable).</summary>
    public long PrescriptionId { get; init; }

    /// <summary>
    /// Indique si le client propriétaire existe. <c>false</c> = aucune écriture. Vaut <c>true</c> par défaut : ce
    /// drapeau ne signale <b>que</b> le refus « client introuvable » et ne prétend rien lorsque la commande a été
    /// rejetée en amont par la validation (le client n'est alors même pas chargé).
    /// </summary>
    public bool CustomerFound { get; init; } = true;

    /// <summary>
    /// Erreurs de validation de commande (P3-1). <b>Vide</b> = commande valide.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (aucune erreur de validation).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
