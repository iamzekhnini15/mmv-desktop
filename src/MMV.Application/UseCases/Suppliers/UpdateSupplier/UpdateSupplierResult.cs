namespace MMV.Application.UseCases.Suppliers.UpdateSupplier;

/// <summary>
/// Sortie (DTO) du use case <see cref="UpdateSupplierUseCase"/>.
/// </summary>
public sealed class UpdateSupplierResult
{
    /// <summary>Indique si le fournisseur visé existait. <c>false</c> = aucune écriture effectuée.</summary>
    public bool SupplierFound { get; init; }

    /// <summary>Identifiant du fournisseur visé (écho de l'entrée).</summary>
    public long SupplierId { get; init; }
}
