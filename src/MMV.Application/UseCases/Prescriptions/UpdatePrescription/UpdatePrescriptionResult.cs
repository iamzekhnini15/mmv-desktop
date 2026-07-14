using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Prescriptions.UpdatePrescription;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdatePrescriptionUseCase"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PrescriptionFound"/> indique si l'ordonnance visée existait. En mode édition, la ViewModel ouvre
/// toujours le formulaire avec une ordonnance réelle (donc <c>true</c> en pratique) ; ce drapeau couvre le cas
/// théorique d'une ordonnance introuvable sans écriture.
/// </para>
/// <para>
/// <b>P3-3B (socle validation).</b> Si l'ordonnance existe mais que la commande est invalide,
/// <see cref="PrescriptionFound"/> vaut <c>true</c>, <see cref="ValidationErrors"/> est renseignée et
/// <see cref="IsValid"/> vaut <c>false</c> : <b>aucune</b> écriture n'a eu lieu et l'entité suivie n'a pas été mutée.
/// </para>
/// <para>
/// <b>Aucun <c>CustomerFound</c> ici</b> : <c>CustomerId</c> est préservé, une ordonnance ne change jamais de
/// propriétaire, et la correction reste permise même si le client est archivé — le client n'est donc pas chargé.
/// </para>
/// </remarks>
public sealed class UpdatePrescriptionResult
{
    /// <summary>Indique si l'ordonnance à modifier a été trouvée. <c>false</c> = aucune écriture effectuée.</summary>
    public bool PrescriptionFound { get; init; }

    /// <summary>Identifiant de l'ordonnance visée (écho de l'entrée).</summary>
    public long PrescriptionId { get; init; }

    /// <summary>
    /// Erreurs de validation de commande (P3-1). <b>Vide</b> = commande valide. Renseignée uniquement quand
    /// l'ordonnance existe (<see cref="PrescriptionFound"/> = <c>true</c>) mais que la commande a été refusée.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (aucune erreur de validation).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
