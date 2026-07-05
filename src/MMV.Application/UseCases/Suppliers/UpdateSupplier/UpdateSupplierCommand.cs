namespace MMV.Application.UseCases.Suppliers.UpdateSupplier;

/// <summary>
/// Entrée (DTO) du use case <see cref="UpdateSupplierUseCase"/> — « Modifier un fournisseur » (P2C-GLOBAL). Porte
/// l'identifiant et les champs modifiés dans <c>SupplierFormViewModel</c> (mode édition).
/// </summary>
/// <remarks>
/// Déplacement iso-fonctionnel de la branche édition de <c>SupplierFormViewModel.SaveAsync</c> (qui appelait
/// directement <c>ISupplierRepository.UpdateAsync</c> + <c>IUnitOfWork.SaveChangesAsync</c>).
/// </remarks>
public sealed class UpdateSupplierCommand
{
    /// <summary>Identifiant du fournisseur à modifier.</summary>
    public long SupplierId { get; init; }

    /// <summary>Nom du fournisseur (requis, validé côté UI).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Adresse e-mail de contact (optionnelle).</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Numéro de téléphone (optionnel).</summary>
    public string? Phone { get; init; }

    /// <summary>Adresse postale (optionnelle).</summary>
    public string? Address { get; init; }

    /// <summary>Code de référence interne (optionnel).</summary>
    public string? ReferenceCode { get; init; }
}
