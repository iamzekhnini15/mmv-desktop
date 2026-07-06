namespace MMV.Application.UseCases.Suppliers.DeleteSupplier;

/// <summary>
/// Entrée (DTO) du use case <see cref="DeleteSupplierUseCase"/> — « Supprimer un fournisseur » (P2C-GLOBAL). Porte
/// l'identifiant du fournisseur à supprimer, tel que sélectionné dans <c>SuppliersViewModel</c>.
/// </summary>
public sealed class DeleteSupplierCommand
{
    /// <summary>Identifiant du fournisseur à supprimer.</summary>
    public long SupplierId { get; init; }
}
