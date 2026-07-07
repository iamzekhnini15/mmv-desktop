using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Customers.UpdateCustomer;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateCustomerUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (notification <c>CustomerSaved</c>, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// <see cref="CustomerFound"/> indique si le client visé existait. En mode édition, la ViewModel ouvre toujours le
/// formulaire avec un client réel (donc <c>true</c> en pratique) ; ce drapeau couvre le cas théorique d'un client
/// introuvable sans écriture (cohérent avec le contrat de <c>DeleteOrderUseCase</c>/<c>UpdateOrderUseCase</c>).
/// <para>
/// <b>P3-1 (socle validation).</b> Si le client existe mais que la commande est invalide,
/// <see cref="CustomerFound"/> vaut <c>true</c>, <see cref="ValidationErrors"/> est renseignée et
/// <see cref="IsValid"/> vaut <c>false</c> : <b>aucune</b> écriture n'a eu lieu. En cas de succès,
/// <see cref="ValidationErrors"/> est vide.
/// </para>
/// </remarks>
public sealed class UpdateCustomerResult
{
    /// <summary>Indique si le client à modifier a été trouvé. <c>false</c> = aucune écriture effectuée.</summary>
    public bool CustomerFound { get; init; }

    /// <summary>Identifiant du client modifié (écho de l'entrée).</summary>
    public long CustomerId { get; init; }

    /// <summary>Nom d'affichage (« Prénom Nom ») du client modifié.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Erreurs de validation de commande (P3-1). <b>Vide</b> = commande valide. Renseignée uniquement quand le
    /// client existe (<see cref="CustomerFound"/> = <c>true</c>) mais que la commande a été refusée.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (aucune erreur de validation).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
