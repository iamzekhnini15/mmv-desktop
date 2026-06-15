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
/// </remarks>
public sealed class CreateCustomerResult
{
    /// <summary>Identifiant attribué au client créé.</summary>
    public long CustomerId { get; init; }

    /// <summary>Nom d'affichage (« Prénom Nom ») du client créé.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
