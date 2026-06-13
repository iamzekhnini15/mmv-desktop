namespace MMV.Application.UseCases.Orders.DeleteOrder;

/// <summary>
/// Résultat du use case « Supprimer une commande » (P2B-2H).
/// Permet à la ViewModel de connaître l'issue de la suppression sans accéder directement à la persistance.
/// </summary>
public sealed class DeleteOrderResult
{
    /// <summary>
    /// Indique si la commande a été trouvée dans la base avant suppression.
    /// <c>false</c> si la commande n'existait pas (suppression silencieuse, cohérent avec le flux d'origine).
    /// </summary>
    public bool OrderFound { get; init; }

    /// <summary>Echo de l'identifiant de la commande traitée.</summary>
    public long OrderId { get; init; }
}
