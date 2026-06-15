namespace MMV.Application.UseCases.Customers.UpdateCustomer;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateCustomerUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (notification <c>CustomerSaved</c>, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// <see cref="CustomerFound"/> indique si le client visé existait. En mode édition, la ViewModel ouvre toujours le
/// formulaire avec un client réel (donc <c>true</c> en pratique) ; ce drapeau couvre le cas théorique d'un client
/// introuvable sans écriture (cohérent avec le contrat de <c>DeleteOrderUseCase</c>/<c>UpdateOrderUseCase</c>).
/// </remarks>
public sealed class UpdateCustomerResult
{
    /// <summary>Indique si le client à modifier a été trouvé. <c>false</c> = aucune écriture effectuée.</summary>
    public bool CustomerFound { get; init; }

    /// <summary>Identifiant du client modifié (écho de l'entrée).</summary>
    public long CustomerId { get; init; }

    /// <summary>Nom d'affichage (« Prénom Nom ») du client modifié.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
