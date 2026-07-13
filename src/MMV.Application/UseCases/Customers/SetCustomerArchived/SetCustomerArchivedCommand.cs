namespace MMV.Application.UseCases.Customers.SetCustomerArchived;

/// <summary>
/// Entrée (DTO) du use case <see cref="SetCustomerArchivedUseCase"/> — « Archiver / réactiver un client »
/// (P3-2B). Porte l'identifiant du client et l'état d'archivage cible (valeur absolue, pas un basculement :
/// l'opération est ainsi idempotente et insensible aux courses multi-poste).
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Modelé sur <c>SetUserActiveCommand</c>.
/// </remarks>
public sealed class SetCustomerArchivedCommand
{
    /// <summary>Identifiant du client concerné.</summary>
    public long CustomerId { get; init; }

    /// <summary>État d'archivage cible : <c>true</c> = archiver, <c>false</c> = réactiver.</summary>
    public bool IsArchived { get; init; }
}
