namespace MMV.Application.UseCases.Suppliers.CreateSupplier;

/// <summary>
/// Entrée (DTO) du use case <see cref="CreateSupplierUseCase"/> — « Créer un fournisseur » (P2C-GLOBAL). Porte les
/// champs saisis dans <c>SupplierFormViewModel</c> ; la ViewModel transforme son état en cette commande, puis délègue.
/// </summary>
/// <remarks>
/// DTO neutre (aucune dépendance EF/Avalonia/MVVM). Déplacement iso-fonctionnel de la branche création de
/// <c>SupplierFormViewModel.SaveAsync</c> (qui appelait directement <c>ISupplierRepository.CreateAsync</c> +
/// <c>IUnitOfWork.SaveChangesAsync</c>).
/// </remarks>
public sealed class CreateSupplierCommand
{
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
