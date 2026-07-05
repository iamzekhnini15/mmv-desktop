namespace MMV.Application.UseCases.Suppliers.CreateSupplier;

/// <summary>
/// Sortie (DTO) du use case <see cref="CreateSupplierUseCase"/>. Permet à la ViewModel de poursuivre son flux
/// (fermeture du formulaire, rafraîchissement de la liste) sans accéder aux détails de persistance.
/// </summary>
public sealed class CreateSupplierResult
{
    /// <summary>Identifiant attribué au fournisseur créé.</summary>
    public long SupplierId { get; init; }

    /// <summary>Nom du fournisseur créé (écho de l'entrée).</summary>
    public string Name { get; init; } = string.Empty;
}
