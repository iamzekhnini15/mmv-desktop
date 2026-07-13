namespace MMV.Application.UseCases.Customers.SetCustomerArchived;

/// <summary>
/// Sortie (DTO) du use case <see cref="SetCustomerArchivedUseCase"/>.
/// </summary>
public sealed class SetCustomerArchivedResult
{
    /// <summary>Indique si le client visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool CustomerFound { get; init; }

    /// <summary>Identifiant du client visé (écho de l'entrée).</summary>
    public long CustomerId { get; init; }

    /// <summary>État d'archivage appliqué (écho de l'entrée si le client a été trouvé).</summary>
    public bool IsArchived { get; init; }
}
