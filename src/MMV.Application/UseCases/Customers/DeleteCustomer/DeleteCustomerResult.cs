namespace MMV.Application.UseCases.Customers.DeleteCustomer;

/// <summary>
/// Sortie (DTO) du use case <see cref="DeleteCustomerUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (retrait de la liste, rafraîchissement) sans accéder aux détails de persistance.
/// </summary>
/// <remarks>
/// <see cref="CustomerFound"/> indique si le client visé existait. Dans le flux d'origine, la suppression n'est
/// déclenchée que sur un client réellement sélectionné (donc <c>true</c> en pratique) ; ce drapeau couvre le cas
/// théorique d'un client introuvable sans écriture (cohérent avec le contrat de <c>DeleteOrderUseCase</c>).
/// </remarks>
public sealed class DeleteCustomerResult
{
    /// <summary>Indique si le client à supprimer a été trouvé. <c>false</c> = aucune écriture effectuée.</summary>
    public bool CustomerFound { get; init; }

    /// <summary>Identifiant du client visé (écho de l'entrée).</summary>
    public long CustomerId { get; init; }
}
