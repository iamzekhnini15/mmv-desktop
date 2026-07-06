namespace MMV.Application.UseCases.Suppliers.DeleteSupplier;

/// <summary>
/// Sortie (DTO) du use case <see cref="DeleteSupplierUseCase"/>.
/// </summary>
public sealed class DeleteSupplierResult
{
    /// <summary>Indique si le fournisseur à supprimer a été trouvé. <c>false</c> = aucune écriture effectuée.</summary>
    public bool SupplierFound { get; init; }

    /// <summary>Identifiant du fournisseur visé (écho de l'entrée).</summary>
    public long SupplierId { get; init; }
}
