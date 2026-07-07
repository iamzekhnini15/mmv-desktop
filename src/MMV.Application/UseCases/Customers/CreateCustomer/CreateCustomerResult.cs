using System;
using System.Collections.Generic;
using MMV.Application.Common;

namespace MMV.Application.UseCases.Customers.CreateCustomer;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateCustomerUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (notification <c>CustomerSaved</c>, rafraîchissement de la liste) sans accéder aux détails de persistance ni
/// faire transiter l'entité ou un type EF.
/// </summary>
/// <remarks>
/// Le flux d'origine n'exploitait aucune valeur de retour (l'événement <c>CustomerSaved</c> transportait l'entité,
/// mais le consommateur se contentait de recharger la liste). Ce résultat expose l'identifiant attribué et un nom
/// d'affichage pratique.
/// <para>
/// <b>P3-1 (socle validation).</b> Si la commande est invalide, <see cref="ValidationErrors"/> est renseignée et
/// <see cref="IsValid"/> vaut <c>false</c> : <b>aucune</b> écriture n'a eu lieu et <see cref="CustomerId"/> reste
/// à 0. En cas de succès, <see cref="ValidationErrors"/> est vide.
/// </para>
/// </remarks>
public sealed class CreateCustomerResult
{
    /// <summary>Identifiant attribué au client créé (0 si la commande a été refusée par la validation).</summary>
    public long CustomerId { get; init; }

    /// <summary>Nom d'affichage (« Prénom Nom ») du client créé.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Erreurs de validation de commande (P3-1). <b>Vide</b> = commande valide et client persisté.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>Indique si la commande était valide (donc si le client a été persisté).</summary>
    public bool IsValid => ValidationErrors.Count == 0;
}
